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

    [Fact]
    public async Task MigrationBackfillsScheduledDatesAndLegacyHistoryCanStillBeRestoredAsync()
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

        await SeedLegacyAsync(database, snapshot, timeout.Token);

        var saved = (await database.Store.ReadAsync("owner-a", snapshot.Id, timeout.Token)).ShouldNotBeNull();
        saved.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(date);
        saved.Transactions.Single(item => item.Id == manual.Id).ScheduledDate.ShouldBeNull();
        saved = await ApplyAsync(database.Store, saved, new UndoChange(saved.Version), timeout.Token);
        saved.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(date);
        saved.Transactions.Single(item => item.Id == occurrence.Id).Memo.ShouldBe("Keep this");
        saved = await ApplyAsync(database.Store, saved, new RedoChange(saved.Version), timeout.Token);
        saved.Transactions.Count.ShouldBe(2);
        saved.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(date);
        BudgetFacts.Balance(saved, account, date).Working.ShouldBe(985);
    }

    private static async Task SeedLegacyAsync(BudgetDatabase database, PlanSnapshot snapshot, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        // Only this test's disposable database is downgraded to exercise a real upgrade.
        await context.GetService<IMigrator>().MigrateAsync("20261003005810_AddBudgeting", token);
        var account = snapshot.Accounts.Single();
        context.Add(new BudgetPlan { Id = snapshot.Id, OwnerId = "owner-a", Name = snapshot.Name, CreatedOn = snapshot.CreatedOn, Version = 1, HistoryCursor = 1 });
        context.Add(new BudgetAccount { Id = account.Id, PlanId = snapshot.Id, Name = account.Name, Kind = account.Kind, OpeningBalance = account.OpeningBalance, OpenedOn = account.OpenedOn });
        var legacyJson = JsonSerializer.Serialize(snapshot, _legacyJson);
        legacyJson.ShouldNotContain("scheduledDate");
        context.Add(new BudgetHistory { Id = Guid.NewGuid(), PlanId = snapshot.Id, Position = 1, Description = "Legacy change", Before = legacyJson, After = legacyJson, Timestamp = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero) });
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
