using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class SelectiveSqlPlanTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReportsReturnBoundedMonthlyAggregatesFromLargeLedgersAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe(captureQueries: true);
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 100_000, 10, 50, 120, token);
        await using (var statistics = await database.Factory.CreateDbContextAsync(token))
        {
            await statistics.Database.ExecuteSqlRawAsync("ANALYZE \"BudgetTransaction\"; ANALYZE \"BudgetSplit\";", token);
        }
        probe.Start();
        var report = await database.Queries.ReportsAsync("owner-a", fixture.Id, fixture.Month.AddMonths(-23), fixture.Month.AddMonths(1).AddDays(-1), token);
        output.WriteLine($"Two-year report: {probe.Stop()}");
        report.Months.Count.ShouldBe(24);
        report.Months[^1].NetWorth.ShouldBe(fixture.Cash);
        report.Spending.Sum(item => item.Amount).ShouldBe(report.Months.Sum(item => item.Expense));
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(0);
        probe.Materialized.GetValueOrDefault(nameof(BudgetSplit)).ShouldBe(0);
        var queries = probe.Queries.Where(item => item.Sql.Contains("\"BudgetTransaction\"", StringComparison.Ordinal)).ToArray();
        queries.Length.ShouldBe(2);
        queries.ShouldAllBe(item => item.Sql.Contains("GROUP BY", StringComparison.Ordinal));
        await ExplainAsync(database, probe, "report", token);
    }

    [Fact]
    public async Task EntryEditsAndRecurringBatchesHaveInspectablePlansAndExactEffectsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe(captureQueries: true);
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 100_000, 10, 50, 120, token);
        await using (var statistics = await database.Factory.CreateDbContextAsync(token))
        {
            await statistics.Database.ExecuteSqlRawAsync("ANALYZE \"BudgetTransaction\"; ANALYZE \"BudgetSplit\"; ANALYZE \"BudgetAccount\";", token);
        }
        var full = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        var entry = full.Transactions[0] with { Memo = "SQL plan observation" };
        probe.Start();
        var edited = (await database.Store.ExecuteAsync("owner-a", fixture.Id, new SaveTransaction(0, entry), token)).AsT0;
        output.WriteLine($"One-entry edit: {probe.Stop()}");
        edited.Transactions.Single().Memo.ShouldBe(entry.Memo);
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(1);
        await ExplainAsync(database, probe, "edit", token);
        var template = entry with
        {
            Id = Guid.Empty,
            Splits = [new(Guid.Empty, fixture.Categories[0].Id, -1, "")],
            Amount = -1,
            Date = full.Today.AddDays(-511),
            Repeat = RepeatFrequency.Daily,
            AnchorDate = full.Today.AddDays(-511),
            Sequence = 0,
        };
        var saved = (await database.Store.ExecuteAsync("owner-a", fixture.Id, new SaveTransaction(1, template), token)).AsT0;
        var templateId = saved.Transactions.Single(item => item.Repeat == RepeatFrequency.Daily).Id;
        probe.Start();
        var posted = (await database.Store.ExecuteAsync("owner-a", fixture.Id, new PostRecurring(2, full.Today), token)).AsT0;
        output.WriteLine($"Recurring batch: {probe.Stop()}");
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(1);
        await ExplainAsync(database, probe, "recurring", token);
        probe.Start();
        (await database.Recurring.RunBatchAsync(token)).ShouldBe(1);
        output.WriteLine($"Background discovery and posting: {probe.Stop()}");
        probe.Queries.Count(item => item.Sql.Contains("\"Repeat\" <> 0", StringComparison.Ordinal)).ShouldBe(2);
        // Keep another batch due while EXPLAIN executes the captured discovery
        // SELECTs, so it measures finding a schedule rather than an empty result.
        await ExplainAsync(database, probe, "background", token);
        var final = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        final.Version.ShouldBe(posted.Version + 1);
        final.Transactions.Count(item => item.SourceTemplateId == templateId).ShouldBe(384);
        final.Transactions.Single(item => item.Id == entry.Id).Memo.ShouldBe(entry.Memo);
        final.Accounts.Sum(account => BudgetFacts.Balance(final, account, final.Today).Working).ShouldBe(fixture.Cash - 384);
        (await database.Recurring.RunBatchAsync(token)).ShouldBe(1);
        (await database.Recurring.RunBatchAsync(token)).ShouldBe(0);
        var complete = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        complete.Version.ShouldBe(posted.Version + 2);
        complete.Transactions.Where(item => item.SourceTemplateId == templateId).Select(item => item.ScheduledDate).Order()
            .ShouldBe(Enumerable.Range(0, 512).Select(day => (DateOnly?)full.Today.AddDays(day - 511)));
        complete.Accounts.Sum(account => BudgetFacts.Balance(complete, account, complete.Today).Working).ShouldBe(fixture.Cash - 512);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Prefixes captured EF-generated SELECT text with constant EXPLAIN; all values remain cloned provider parameters and the connection is a disposable test fixture.")]
    private async Task ExplainAsync(BudgetDatabase database, DatabaseProbe probe, string label, CancellationToken token)
    {
        await using var context = await database.Factory.CreateDbContextAsync(token);
        await context.Database.OpenConnectionAsync(token);
        // EXPLAIN ANALYZE executes only captured SELECTs against the disposable
        // fixture. No write command or development connection reaches this path.
        foreach (var query in probe.Queries.Where(item => item.Sql.Contains("\"BudgetTransaction\"", StringComparison.Ordinal)
            || item.Sql.Contains("\"BudgetSplit\"", StringComparison.Ordinal)))
        {
            await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query.Sql, (NpgsqlConnection)context.Database.GetDbConnection());
            command.Parameters.AddRange(query.Parameters.Select(item => item.Clone()).ToArray());
            var json = ((string?)(await command.ExecuteScalarAsync(token))).ShouldNotBeNull();
            using var plan = JsonDocument.Parse(json);
            output.WriteLine($"{label}: original SQL execution {query.Duration.TotalMilliseconds:F1} ms\n{query.Sql}\n{plan.RootElement}");
            plan.RootElement[0].GetProperty("Execution Time").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
            if (label is "edit") { AssertBoundedRead(plan.RootElement[0].GetProperty("Plan"), 99); }
            if (label is "report")
            {
                // Database scans still process ledger history, but returned rows
                // grow with accounts/categories and the requested months only.
                var maximum = query.Sql.Contains("UNION ALL", StringComparison.Ordinal) ? 10 * 25 : 51 * 24;
                plan.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble().ShouldBeLessThanOrEqualTo(maximum);
            }
            if (query.Sql.Contains("JOIN unnest", StringComparison.Ordinal))
            {
                AssertBoundedRead(plan.RootElement[0].GetProperty("Plan"), RecurrencePlanner.BatchSize);
            }
            if (query.Sql.Contains("\"Repeat\" <> 0", StringComparison.Ordinal))
            {
                // Discovery should examine templates, not discard the 100k posted
                // entries to locate one schedule. Check SQL work, not just EF counts.
                AssertBoundedRead(plan.RootElement[0].GetProperty("Plan"), RecurrencePlanner.BatchSize);
            }
        }
    }

    private static void AssertBoundedRead(JsonElement node, int maximum)
    {
        node.GetProperty("Actual Rows").GetDouble().ShouldBeLessThanOrEqualTo(maximum);
        if (node.TryGetProperty("Rows Removed by Filter", out var discarded)) { discarded.GetDouble().ShouldBeLessThanOrEqualTo(maximum); }
        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray()) { AssertBoundedRead(child, maximum); }
        }
    }
}
