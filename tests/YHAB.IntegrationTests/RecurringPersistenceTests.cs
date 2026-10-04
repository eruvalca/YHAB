using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class RecurringPersistenceTests
{
    private static readonly JsonSerializerOptions _legacyJson = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [Fact]
    public async Task OccurrencesCanShareEditedDatesWithoutLosingIdentityOrHistoryAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var store = database.Store;
        var id = await store.CreateAsync("owner-a", new("Recurring dates"), timeout.Token);
        var plan = (await store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        var start = plan.Today.AddDays(-2);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, start, false, "");
        plan = await ApplyAsync(store, plan, new SaveAccount(plan.Version, account), timeout.Token);
        var template = new TransactionData(Guid.NewGuid(), account.Id, start, "Daily", "", -25, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.NewGuid(), plan.Categories[0].Id, -25, "")], RepeatFrequency.Daily);
        plan = await ApplyAsync(store, plan, new SaveTransaction(plan.Version, template), timeout.Token);
        var occurrence = plan.Transactions.Single(item => item.SourceTemplateId == template.Id && item.Date == start);

        plan = await ApplyAsync(store, plan, new SaveTransaction(plan.Version, occurrence with { Date = start.AddDays(1), ScheduledDate = null }), timeout.Token);

        plan.Transactions.Count(item => item.SourceTemplateId == template.Id && item.Date == start.AddDays(1)).ShouldBe(2);
        plan.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(start);
        plan = await ApplyAsync(store, plan, new UndoChange(plan.Version), timeout.Token);
        plan.Transactions.Single(item => item.Id == occurrence.Id).Date.ShouldBe(start);
        plan = await ApplyAsync(store, plan, new RedoChange(plan.Version), timeout.Token);
        plan.Transactions.Single(item => item.Id == occurrence.Id).Date.ShouldBe(start.AddDays(1));
        plan.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(start);
        var next = plan.Transactions.Single(item => item.Id == template.Id);
        plan = await ApplyAsync(store, plan, new SaveTransaction(plan.Version, next with { Date = start }), timeout.Token);
        plan.Transactions.Count.ShouldBe(4);
        plan.Transactions.Where(item => item.SourceTemplateId == template.Id).Select(item => item.ScheduledDate).Order()
            .ShouldBe([start, start.AddDays(1), start.AddDays(2)]);
        BudgetFacts.Balance(plan, account, plan.Today).Working.ShouldBe(925);

        await using var context = await database.Factory.CreateDbContextAsync(timeout.Token);
        context.Add(new BudgetTransaction
        {
            Id = Guid.NewGuid(),
            PlanId = id,
            AccountId = account.Id,
            Date = plan.Today,
            SourceTemplateId = template.Id,
            ScheduledDate = start,
        });
        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(timeout.Token));
        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationBackfillsScheduledDatesAndLegacyHistoryCanStillBeRestoredAsync(bool dateChanged)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var date = new DateOnly(2026, 10, 1);
        var account = new AccountData(Guid.NewGuid(), "Tracking", AccountKind.Asset, 1000, date, false, "");
        var occurrence = new TransactionData(Guid.NewGuid(), account.Id, date, "Legacy recurrence", "Keep this", -25, null,
            ClearingState.Uncleared, ClearingState.Uncleared, true, "", [], SourceTemplateId: Guid.NewGuid());
        var manual = occurrence with { Id = Guid.NewGuid(), SourceTemplateId = null, Payee = "Manual", Amount = 10 };
        var snapshot = new PlanSnapshot(Guid.NewGuid(), "Legacy plan", "", date, 0, [account], [], [], [], [occurrence, manual], false, false, []);

        var before = snapshot with
        {
            Transactions = [occurrence with { Memo = "Before the edit", Date = dateChanged ? date.AddDays(-1) : date }, manual],
        };
        await SeedLegacyAsync(database, before, snapshot, timeout.Token);

        var saved = (await database.Store.ReadAsync("owner-a", snapshot.Id, timeout.Token)).ShouldNotBeNull();
        saved.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(date);
        saved.Transactions.Single(item => item.Id == manual.Id).ScheduledDate.ShouldBeNull();
        saved = await ApplyAsync(database.Store, saved, new UndoChange(saved.Version), timeout.Token);
        saved.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(date);
        saved.Transactions.Single(item => item.Id == occurrence.Id).Memo.ShouldBe("Before the edit");
        saved.Transactions.Single(item => item.Id == occurrence.Id).Date.ShouldBe(dateChanged ? date.AddDays(-1) : date);
        saved.Transactions.Single(item => item.Id == manual.Id).ScheduledDate.ShouldBeNull();
        saved = await ApplyAsync(database.Store, saved, new RedoChange(saved.Version), timeout.Token);
        saved.Transactions.Count.ShouldBe(2);
        saved.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(date);
        saved.Transactions.Single(item => item.Id == occurrence.Id).Memo.ShouldBe("Keep this");
        saved.Transactions.Single(item => item.Id == occurrence.Id).Date.ShouldBe(date);
        BudgetFacts.Balance(saved, account, date).Working.ShouldBe(985);

        // Normalizing missing identity fields must not hide a real later edit.
        await using var context = await database.Factory.CreateDbContextAsync(timeout.Token);
        var changed = await context.Set<BudgetTransaction>().SingleAsync(item => item.Id == occurrence.Id, timeout.Token);
        changed.Memo = "Later edit";
        await context.SaveChangesAsync(timeout.Token);
        (await database.Store.ExecuteAsync("owner-a", saved.Id, new UndoChange(saved.Version), timeout.Token)).IsT1.ShouldBeTrue();
        var conflicted = (await database.Store.ReadAsync("owner-a", saved.Id, timeout.Token)).ShouldNotBeNull();
        conflicted.Version.ShouldBe(saved.Version);
        conflicted.Transactions.Single(item => item.Id == occurrence.Id).Memo.ShouldBe("Later edit");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyOccurrenceCreationAndDeletionRestoreStableIdentityAsync(bool deleted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var date = new DateOnly(2026, 10, 1);
        var account = new AccountData(Guid.NewGuid(), "Tracking", AccountKind.Asset, 1000, date, false, "");
        var occurrence = new TransactionData(Guid.NewGuid(), account.Id, date, "Legacy occurrence", "", -25, null,
            ClearingState.Uncleared, ClearingState.Uncleared, true, "", [], SourceTemplateId: Guid.NewGuid());
        var present = new PlanSnapshot(Guid.NewGuid(), "Legacy plan", "", date, 0, [account], [], [], [], [occurrence], false, false, []);
        var absent = present with { Transactions = [] };
        await SeedLegacyAsync(database, deleted ? present : absent, deleted ? absent : present, token);
        var saved = (await database.Store.ReadAsync("owner-a", present.Id, token)).ShouldNotBeNull();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            saved = await ApplyAsync(database.Store, saved, new UndoChange(saved.Version), token);
            saved.Transactions.Count.ShouldBe(deleted ? 1 : 0);
            if (deleted)
            {
                saved.Transactions.Single().ScheduledDate.ShouldBe(date);
                saved.Transactions.Single().Sequence.ShouldBe(0);
            }
            saved = await ApplyAsync(database.Store, saved, new RedoChange(saved.Version), token);
            saved.Transactions.Count.ShouldBe(deleted ? 0 : 1);
            if (!deleted)
            {
                saved.Transactions.Single().ScheduledDate.ShouldBe(date);
                saved.Transactions.Single().Sequence.ShouldBe(0);
            }
        }
    }

    [Fact]
    public async Task DeletingMigratedTransactionCanUndoAndRedoWithoutChangingSequenceAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var date = new DateOnly(2026, 10, 1);
        var account = new AccountData(Guid.NewGuid(), "Tracking", AccountKind.Asset, 1000, date, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, date, "Legacy entry", "", -25, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", []);
        var other = entry with { Id = Guid.NewGuid(), Amount = 10 };
        var original = new PlanSnapshot(Guid.NewGuid(), "Legacy plan", "", date, 0, [account], [], [], [], [entry, other], false, false, []);
        await SeedLegacyAsync(database, original, original, token);
        var saved = (await database.Store.ReadAsync("owner-a", original.Id, token)).ShouldNotBeNull();
        saved.Transactions.ShouldAllBe(item => item.Sequence == 0);
        saved = await ApplyAsync(database.Store, saved, new DeleteTransactions(saved.Version, [entry.Id]), token);
        saved.Transactions.Single().Id.ShouldBe(other.Id);
        saved = await ApplyAsync(database.Store, saved, new UndoChange(saved.Version), token);
        saved.Transactions.Count.ShouldBe(2);
        saved.Transactions.ShouldAllBe(item => item.Sequence == 0);
        BudgetFacts.Balance(saved, account, date).Working.ShouldBe(985);
        saved = await ApplyAsync(database.Store, saved, new RedoChange(saved.Version), token);
        saved.Transactions.Single().Id.ShouldBe(other.Id);
        BudgetFacts.Balance(saved, account, date).Working.ShouldBe(1010);
        saved = await ApplyAsync(database.Store, saved, new UndoChange(saved.Version), token);
        saved.Transactions.ShouldAllBe(item => item.Sequence == 0);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        (await context.Set<BudgetPlan>().SingleAsync(item => item.Id == original.Id, token)).NextSequence.ShouldBe(0);
    }

    private static async Task SeedLegacyAsync(BudgetDatabase database, PlanSnapshot before, PlanSnapshot snapshot, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        // Only this test's disposable database is downgraded to exercise a real upgrade.
        await context.GetService<IMigrator>().MigrateAsync("20261003005810_AddBudgeting", token);
        var account = snapshot.Accounts.Single();
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "BudgetPlans" ("Id", "OwnerId", "Name", "Notes", "CreatedOn", "Version", "HistoryCursor")
            VALUES ({snapshot.Id}, 'owner-a', {snapshot.Name}, '', {snapshot.CreatedOn}, 1, 1)
            """, token);
        context.Add(new BudgetAccount { Id = account.Id, PlanId = snapshot.Id, Name = account.Name, Kind = account.Kind, OpeningBalance = account.OpeningBalance, OpenedOn = account.OpenedOn });
        var legacyJson = JsonSerializer.Serialize(snapshot, _legacyJson);
        var beforeJson = JsonSerializer.Serialize(before, _legacyJson);
        legacyJson.ShouldNotContain("scheduledDate");
        beforeJson.ShouldNotContain("scheduledDate");
        context.Add(new BudgetHistory { Id = Guid.NewGuid(), PlanId = snapshot.Id, Position = 1, Description = "Legacy change", Before = beforeJson, After = legacyJson, Timestamp = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero) });
        await context.SaveChangesAsync(token);
        foreach (var entry in snapshot.Transactions)
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO "BudgetTransaction" ("PlanId", "Id", "AccountId", "Date", "Payee", "Memo", "Amount",
                    "State", "TransferState", "NeedsApproval", "Flag", "Repeat", "Occurrence", "SourceTemplateId")
                VALUES ({snapshot.Id}, {entry.Id}, {entry.AccountId}, {entry.Date}, {entry.Payee}, {entry.Memo}, {entry.Amount},
                    0, 0, {entry.NeedsApproval}, '', 0, 0, {entry.SourceTemplateId})
                """, token);
        }
        await context.GetService<IMigrator>().MigrateAsync(cancellationToken: token);
        (await context.Database.GetPendingMigrationsAsync(token)).ShouldBeEmpty();
    }

    private static async Task<PlanSnapshot> ApplyAsync(BudgetStore store, PlanSnapshot plan, PlanCommand command, CancellationToken token)
    {
        (await store.ExecuteAsync("owner-a", plan.Id, command, token)).IsT0.ShouldBeTrue();
        return (await store.ReadAsync("owner-a", plan.Id, token)).ShouldNotBeNull();
    }
}
