using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class LedgerFoundationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LargePlansUseBoundedReadsAndSmallHistoryWithExactTotalsAsync()
    {
        // Keep the three observations sequential within one test so process-wide
        // allocation measurements do not include another threshold's fixture.
        foreach (var count in new[] { 10_000, 50_000, 100_000 })
        {
            await VerifyLargePlanAsync(count);
        }
    }

    private async Task VerifyLargePlanAsync(int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));
        var token = timeout.Token;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var id = await database.Store.CreateAsync("owner-a", new("Ten-year ledger"), token);
        var plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1_000_000, plan.Today.AddYears(-10), false, "");
        (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(plan.Version, account), token)).IsT0.ShouldBeTrue();
        await SeedAsync(database, id, account, plan.Categories[0].Id, count, token);
        var timer = Stopwatch.StartNew();
        var view = await database.Queries.ViewAsync("owner-a", id, token);
        var viewTime = timer.ElapsedMilliseconds;
        view.Catalog.Transactions.ShouldBeEmpty();
        view.Balances.Single().Working.ShouldBe(1_000_000 + count / 10 * 100 - count / 10 * 9);
        var viewBytes = JsonSerializer.SerializeToUtf8Bytes(view).Length;
        viewBytes.ShouldBeLessThan(30_000);
        foreach (var sort in new[] { "Newest first", "Oldest first", "Payee", "Amount" })
        {
            timer.Restart();
            var first = await database.Queries.RegisterAsync("owner-a", id, new(account.Id, Sort: sort, Version: view.Catalog.Version), token);
            first.Rows.Count.ShouldBe(50);
            first.Total.ShouldBe(count);
            first.Next.ShouldNotBeNull();
            var second = await database.Queries.RegisterAsync("owner-a", id, new(account.Id, Sort: sort, After: first.Next, Version: view.Catalog.Version), token);
            second.Rows.Count.ShouldBe(50);
            first.Rows.Select(item => item.Transaction.Id).Intersect(second.Rows.Select(item => item.Transaction.Id)).ShouldBeEmpty();
            JsonSerializer.SerializeToUtf8Bytes(first).Length.ShouldBeLessThan(60_000);
            output.WriteLine($"{count:N0} transactions, {sort}: two 50-row pages {timer.ElapsedMilliseconds} ms; catalog {viewTime} ms / {viewBytes} bytes.");
        }
        timer.Restart();
        var full = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var month = BudgetFacts.Month(full.Today);
        var expected = BudgetCalculator.Calculate(full, month, full.Today);
        probe.Start();
        var projected = await database.Queries.MonthAsync("owner-a", id, month, full.Version, token);
        output.WriteLine($"{count:N0} cold month: {probe.Stop()}.");
        projected.ReadyToAssign.ShouldBe(expected.ReadyToAssign);
        projected.Available.ShouldBe(expected.Available);
        output.WriteLine($"{count:N0} transactions: full read, independent comparison and first month projection {timer.ElapsedMilliseconds} ms; full payload {JsonSerializer.SerializeToUtf8Bytes(full).Length} bytes.");
        var assigned = new AssignMoney(full.Version, full.Categories[0].Id, month, 123.45m);
        probe.Start();
        (await database.Store.ExecuteAsync("owner-a", id, assigned, token)).IsT0.ShouldBeTrue();
        output.WriteLine($"{count:N0} assignment save: {probe.Stop()}.");
        probe.Commands.ShouldAllBe(sql => !sql.Contains("FROM \"BudgetTransaction\"", StringComparison.Ordinal));
        probe.Start();
        var resumed = await database.Queries.MonthAsync("owner-a", id, month, full.Version + 1, token);
        output.WriteLine($"{count:N0} checkpoint month after assignment: {probe.Stop()}.");
        resumed.ReadyToAssign.ShouldBe(expected.ReadyToAssign - 123.45m);
        resumed.Assigned.ShouldBe(123.45m);
        var updated = await database.Queries.ViewAsync("owner-a", id, token);
        (await database.Store.ExecuteAsync("owner-a", id, assigned, token)).IsT0.ShouldBeTrue();
        (await database.Queries.ViewAsync("owner-a", id, token)).Catalog.Version.ShouldBe(updated.Catalog.Version);
        (await database.Store.ExecuteAsync("owner-a", id, assigned with { Amount = 99 }, token)).IsT1.ShouldBeTrue();
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var history = await context.Set<BudgetHistory>().Where(item => item.PlanId == id).OrderByDescending(item => item.Position).FirstAsync(token);
        (history.Before.Length + history.After.Length).ShouldBeLessThan(2000);
        (await context.Set<BudgetTransaction>().CountAsync(item => item.PlanId == id, token)).ShouldBe(count);
        (await database.Store.ExecuteAsync("owner-a", id, new UndoChange(updated.Catalog.Version), token)).IsT0.ShouldBeTrue();
        var undone = await database.Queries.ViewAsync("owner-a", id, token);
        undone.Catalog.Allocations.ShouldBeEmpty();
        undone.Balances.ShouldBe(view.Balances);
        var stale = await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.MonthAsync("owner-a", id, month, full.Version, token));
        stale.Status.ShouldBe(409);
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.ViewAsync("owner-b", id, token))).Status.ShouldBe(404);
    }

    [Fact]
    public async Task BackgroundPostingIsBoundedIdempotentAndPreservesUserUndoAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var id = await database.Store.CreateAsync("owner-a", new("Background"), token);
        var plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, plan.Today.AddYears(-1), false, "");
        (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(plan.Version, account), token)).IsT0.ShouldBeTrue();
        plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var template = new TransactionData(Guid.Empty, account.Id, plan.Today.AddDays(-259), "Daily", "", -1, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.Empty, plan.Categories[0].Id, -1, "")], RepeatFrequency.Daily);
        (await database.Store.ExecuteAsync("owner-a", id, new SaveTransaction(plan.Version, template), token)).IsT0.ShouldBeTrue();
        plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        plan.Transactions.Count.ShouldBe(129);
        plan.Transactions.ShouldAllBe(item => item.Id.Version == 7 && item.Sequence > 0);
        (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(plan.Version, plan.Categories[0].Id, BudgetFacts.Month(plan.Today), 40), token)).IsT0.ShouldBeTrue();
        await Task.WhenAll(database.Recurring.RunBatchAsync(token), database.Recurring.RunBatchAsync(token));
        await database.Recurring.RunBatchAsync(token);
        (await database.Recurring.RunBatchAsync(token)).ShouldBe(0);
        var posted = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        posted.Transactions.Count.ShouldBe(261);
        posted.Transactions.Select(item => item.Sequence).Distinct().Count().ShouldBe(261);
        posted.Transactions.Where(item => item.SourceTemplateId.HasValue).Select(item => item.ScheduledDate).Distinct().Count().ShouldBe(260);
        posted.Changes.Count.ShouldBe(3);
        var sequences = posted.Transactions.ToDictionary(item => item.Id, item => item.Sequence);
        (await database.Store.ExecuteAsync("owner-a", id, new UndoChange(posted.Version), token)).IsT0.ShouldBeTrue();
        var undone = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        undone.Transactions.Count.ShouldBe(261);
        undone.Allocations.ShouldBeEmpty();
        (await database.Store.ExecuteAsync("owner-a", id, new RedoChange(undone.Version), token)).IsT0.ShouldBeTrue();
        var restored = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        restored.Allocations.Single().Amount.ShouldBe(40);
        restored.Transactions.ShouldAllBe(item => item.Sequence == sequences[item.Id]);
    }

    [Fact]
    public async Task BackgroundPostingAndAssignmentContendWithoutLostMoneyOrOccurrencesAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var barrier = new PlanClaimBarrier(2);
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: barrier);
        var id = await database.Store.CreateAsync("owner-a", new("Worker contention"), token);
        var plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, plan.Today.AddYears(-1), false, "");
        (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(0, account), token)).IsT0.ShouldBeTrue();
        var template = new TransactionData(Guid.Empty, account.Id, plan.Today.AddDays(-259), "Daily", "", -1, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.Empty, plan.Categories[0].Id, -1, "")], RepeatFrequency.Daily);
        (await database.Store.ExecuteAsync("owner-a", id, new SaveTransaction(1, template), token)).IsT0.ShouldBeTrue();
        barrier.Armed = true;
        var timer = Stopwatch.StartNew();
        var edit = database.Store.ExecuteAsync("owner-a", id, new AssignMoney(2, plan.Categories[0].Id, BudgetFacts.Month(plan.Today), 40), token);
        var posting = database.Recurring.RunBatchAsync(token);
        await Task.WhenAll(edit, posting);
        var userResult = await edit;
        var workerCommits = await posting;
        ((userResult.IsT0 ? 1 : 0) + workerCommits).ShouldBe(1);
        userResult.IsT3.ShouldBe(workerCommits == 1);
        output.WriteLine($"Forced worker/user same-revision contention: {timer.ElapsedMilliseconds} ms; worker committed {workerCommits}, user conflict {userResult.IsT3}.");
        barrier.Armed = false;
        if (userResult.IsT3)
        {
            (await database.Store.ExecuteAsync("owner-a", id, new AssignMoney(3, plan.Categories[0].Id, BudgetFacts.Month(plan.Today), 40), token)).IsT0.ShouldBeTrue();
        }
        await database.Recurring.RunBatchAsync(token);
        await database.Recurring.RunBatchAsync(token);
        var final = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        final.Allocations.Single().Amount.ShouldBe(40);
        final.Transactions.Count.ShouldBe(261);
        final.Transactions.Where(item => item.SourceTemplateId.HasValue).Select(item => item.ScheduledDate).Distinct().Count().ShouldBe(260);
        BudgetFacts.Balance(final, account, final.Today).Working.ShouldBe(740);
        final.Changes.Count.ShouldBe(3);
    }

    private static async Task SeedAsync(BudgetDatabase database, Guid id, AccountData account, Guid category, int count, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        // Synthetic bulk input only; all reads and behavior probes use real services and PostgreSQL constraints.
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "BudgetTransaction" ("PlanId", "Id", "AccountId", "Date", "Payee", "Memo", "Amount", "State", "TransferState",
                "NeedsApproval", "Flag", "Repeat", "Occurrence", "Sequence")
            SELECT {id}, gen_random_uuid(), {account.Id}, {account.OpenedOn} + (n % 3650)::int,
                CASE WHEN n % 10 = 0 THEN 'Salary' ELSE 'Market' END, '', CASE WHEN n % 10 = 0 THEN 100 ELSE -1 END,
                0, 0, false, '', 0, 0, n FROM generate_series(1, {count}) n;
            INSERT INTO "BudgetSplit" ("PlanId", "Id", "TransactionId", "CategoryId", "Amount", "Memo")
            SELECT "PlanId", gen_random_uuid(), "Id", CASE WHEN "Amount" < 0 THEN {category} ELSE NULL END, "Amount", ''
            FROM "BudgetTransaction" WHERE "PlanId" = {id};
            UPDATE "BudgetPlans" SET "NextSequence" = {count} WHERE "Id" = {id};
            """, token);
    }

    [Fact]
    public async Task PostingFailureDoesNotAbandonOtherPlansAndClosedAccountsStayPausedAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var plans = new List<Guid>();
        foreach (var name in new[] { "Invalid stored schedule", "Healthy schedule", "Closed account" })
        {
            var id = await database.Store.CreateAsync("owner-a", new(name), token);
            plans.Add(id);
            var plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
            var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 0, plan.Today, false, "");
            (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(plan.Version, account), token)).IsT0.ShouldBeTrue();
            plan = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
            var template = new TransactionData(Guid.Empty, account.Id, plan.Today.AddDays(1), "Salary", "", 10, null,
                ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.Empty, null, 10, "")], RepeatFrequency.Daily);
            (await database.Store.ExecuteAsync("owner-a", id, new SaveTransaction(plan.Version, template), token)).IsT0.ShouldBeTrue();
            await using var context = await database.Factory.CreateDbContextAsync(token);
            var stored = await context.Set<BudgetTransaction>().SingleAsync(item => item.PlanId == id, token);
            stored.Date = plan.Today;
            stored.AnchorDate = plan.Today;
            // Direct corruption verifies that one failing plan cannot prevent a healthy plan posting.
            if (plans.Count == 1) { stored.Repeat = (RepeatFrequency)99; }
            if (plans.Count == 3) { (await context.Set<BudgetAccount>().SingleAsync(item => item.PlanId == id, token)).Closed = true; }
            await context.SaveChangesAsync(token);
        }
        (await database.Recurring.RunBatchAsync(token)).ShouldBe(1);
        (await database.Store.ReadAsync("owner-a", plans[0], token)).ShouldNotBeNull().Transactions.Count.ShouldBe(1);
        var healthy = (await database.Store.ReadAsync("owner-a", plans[1], token)).ShouldNotBeNull();
        healthy.Transactions.Count.ShouldBe(2);
        healthy.Transactions.Single(item => item.SourceTemplateId.HasValue).NeedsApproval.ShouldBeTrue();
        (await database.Store.ReadAsync("owner-a", plans[2], token)).ShouldNotBeNull().Transactions.Count.ShouldBe(1);
        (await database.Recurring.RunBatchAsync(token)).ShouldBe(0);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => database.Recurring.RunBatchAsync(canceled.Token));
    }
}
