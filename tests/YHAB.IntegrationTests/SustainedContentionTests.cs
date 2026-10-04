using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class SustainedContentionTests(ITestOutputHelper output)
{
    private const int Templates = 4;
    private const int Occurrences = 365;

    [Fact]
    public async Task SustainedReplicasPreserveEveryUserWriteAndRecurringOccurrenceAsync()
    {
        foreach (var load in new[] { (Editors: 1, Workers: 1, Paced: false), (Editors: 4, Workers: 2, Paced: false), (Editors: 8, Workers: 3, Paced: false), (Editors: 8, Workers: 3, Paced: true) })
        {
            await ObserveAsync(load.Editors, load.Workers, load.Paced);
        }
    }

    private async Task ObserveAsync(int editors, int workers, bool paced)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixtures = new List<LedgerScaleFixture>();
        for (var index = 0; index < 3; index++)
        {
            var fixture = await LedgerScaleFixture.CreateAsync(database, 10_000, 10, 10, 60, token);
            await AddDueTemplatesAsync(database, fixture, token);
            fixtures.Add(fixture);
        }
        using var logs = new ErrorLogs();
        var watch = new Stopwatch();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latency = new ConcurrentBag<double>();
        var attempts = new ConcurrentBag<double>();
        var userReceipts = new ConcurrentBag<(Guid PlanId, Guid OperationId)>();
        var completed = new int[editors];
        var conflicts = 0;
        var batches = 0;
        var postedBatches = 0;
        var reads = 0;
        using var process = Process.GetCurrentProcess();
        var startingPrivate = process.PrivateMemorySize64;
        var startingWorkingSet = process.WorkingSet64;
        var peakPrivate = startingPrivate;
        var peakWorkingSet = startingWorkingSet;
        var startingAllocated = GC.GetTotalAllocatedBytes(precise: true);
        var startingCollections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        const int Seconds = 60;
        async Task SampleMemoryAsync()
        {
            using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(1));
            await start.Task.WaitAsync(token);
            do
            {
                process.Refresh();
                peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                await cadence.WaitForNextTickAsync(token);
            } while (watch.Elapsed < TimeSpan.FromSeconds(Seconds));
        }
        async Task EditAsync(int editor)
        {
            await using var peer = database.CreatePeerServices(logs);
            var store = peer.GetRequiredService<BudgetStore>();
            var queries = peer.GetRequiredService<BudgetQueries>();
            var fixture = fixtures[editor % fixtures.Count];
            // Deliberate offered-load pacing, never a wait for correctness or readiness.
            using var cadence = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            await start.Task.WaitAsync(token);
            while (watch.Elapsed < TimeSpan.FromSeconds(Seconds) || completed[editor] == 0)
            {
                var operation = Stopwatch.StartNew();
                var operationId = Guid.CreateVersion7();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var attempt = Stopwatch.StartNew();
                    var revision = await queries.RevisionAsync("owner-a", fixture.Id, token);
                    var command = new AssignMoney(revision, fixture.Categories[editor].Id, fixture.Month, completed[editor] + 2) { OperationId = operationId };
                    var result = await store.ExecuteAsync("owner-a", fixture.Id, command, token);
                    attempts.Add(attempt.Elapsed.TotalMilliseconds);
                    if (result.IsT3) { Interlocked.Increment(ref conflicts); continue; }
                    result.IsT0.ShouldBeTrue();
                    result.AsT0.Version.ShouldBe(revision + 1);
                    userReceipts.Add((fixture.Id, operationId));
                    completed[editor]++;
                    latency.Add(operation.Elapsed.TotalMilliseconds);
                    break;
                }
                if (paced) { await cadence.WaitForNextTickAsync(token); }
            }
        }
        async Task PostAsync()
        {
            await using var peer = database.CreatePeerServices(logs);
            var processor = peer.GetRequiredService<RecurringProcessor>();
            await start.Task.WaitAsync(token);
            do
            {
                Interlocked.Add(ref postedBatches, await processor.RunBatchAsync(token));
                Interlocked.Increment(ref batches);
            } while (watch.Elapsed < TimeSpan.FromSeconds(Seconds));
        }
        async Task ReadAsync()
        {
            await using var peer = database.CreatePeerServices(logs);
            var queries = peer.GetRequiredService<BudgetQueries>();
            await start.Task.WaitAsync(token);
            do
            {
                foreach (var fixture in fixtures)
                {
                    var page = await queries.RegisterAsync("owner-a", fixture.Id, new(null), token);
                    page.Rows.Count.ShouldBe(50);
                    page.Rows.Select(item => item.Transaction.Id).Distinct().Count().ShouldBe(50);
                    Interlocked.Increment(ref reads);
                }
            } while (watch.Elapsed < TimeSpan.FromSeconds(Seconds));
        }
        var tasks = Enumerable.Range(0, editors).Select(EditAsync).Concat(Enumerable.Range(0, workers).Select(_ => PostAsync())).Append(ReadAsync()).Append(SampleMemoryAsync()).ToArray();
        watch.Start();
        start.SetResult();
        await Task.WhenAll(tasks);
        watch.Stop();
        process.Refresh();
        peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
        output.WriteLine($"Load process memory: private start={startingPrivate:N0} B, sampled peak={peakPrivate:N0} B, end={process.PrivateMemorySize64:N0} B; working set start={startingWorkingSet:N0} B, sampled peak={peakWorkingSet:N0} B, end={process.WorkingSet64:N0} B; allocated={GC.GetTotalAllocatedBytes(precise: true) - startingAllocated:N0} B; GC collections=[{string.Join(",", Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - startingCollections[generation]))}]. One-second samples include the test harness; peaks between samples may be higher.");
        var pending = 0;
        await using (var context = await database.Factory.CreateDbContextAsync(token))
        {
            pending = await context.Set<BudgetTransaction>().CountAsync(item => item.Repeat != RepeatFrequency.None && item.Date <= new DateOnly(2026, 10, 2), token);
        }
        // Completion must happen while editors are still active, not only in the
        // quiet drain below. Each plan has 1,460 dates across 12 bounded batches.
        pending.ShouldBe(0);
        postedBatches.ShouldBe(36);
        var drain = Stopwatch.StartNew();
        var remaining = 1;
        var drainedBatches = 0;
        for (var batch = 0; remaining > 0 && batch < 100; batch++)
        {
            remaining = await database.Recurring.RunBatchAsync(token);
            drainedBatches += remaining;
        }
        remaining.ShouldBe(0);
        drain.Stop();
        logs.Errors.ShouldBeEmpty();
        completed.ShouldAllBe(count => count > 0);
        reads.ShouldBeGreaterThan(0);
        long versions = 0;
        foreach (var fixture in fixtures)
        {
            var final = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
            versions += final.Version;
            var occurrences = final.Transactions.Where(item => item.SourceTemplateId.HasValue).ToArray();
            occurrences.Length.ShouldBe(Templates * Occurrences);
            occurrences.Select(item => (item.SourceTemplateId, item.ScheduledDate)).Distinct().Count().ShouldBe(Templates * Occurrences);
            occurrences.ShouldAllBe(item => item.Amount == -1 && item.NeedsApproval);
            foreach (var template in final.Transactions.Where(item => item.Repeat != RepeatFrequency.None))
            {
                template.Date.ShouldBe(final.Today.AddDays(1));
                var dates = occurrences.Where(item => item.SourceTemplateId == template.Id).Select(item => item.ScheduledDate!.Value).Order().ToArray();
                dates.ShouldBe(Enumerable.Range(0, Occurrences).Select(day => final.Today.AddDays(day + 1 - Occurrences)).ToArray());
            }
            final.Transactions.Select(item => item.Sequence).Distinct().Count().ShouldBe(final.Transactions.Count);
            final.Accounts.Sum(account => BudgetFacts.Balance(final, account, final.Today).Working).ShouldBe(fixture.Cash - Templates * Occurrences);
            for (var editor = 0; editor < editors; editor++)
            {
                if (fixtures[editor % fixtures.Count].Id == fixture.Id)
                {
                    final.Allocations.Single(item => item.Month == fixture.Month && item.CategoryId == fixture.Categories[editor].Id).Amount.ShouldBe(completed[editor] + 1);
                }
            }
            await using var context = await database.Factory.CreateDbContextAsync(token);
            (await context.Set<BudgetReceipt>().LongCountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe(final.Version);
            var receiptIds = (await context.Set<BudgetReceipt>().Where(item => item.PlanId == fixture.Id).Select(item => item.OperationId).ToArrayAsync(token)).ToHashSet();
            userReceipts.Where(item => item.PlanId == fixture.Id).ShouldAllBe(item => receiptIds.Contains(item.OperationId));
            var userWrites = Enumerable.Range(0, editors).Where(editor => fixtures[editor % fixtures.Count].Id == fixture.Id).Sum(editor => completed[editor]);
            (await context.Set<BudgetHistory>().CountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe(Math.Min(50, userWrites));
        }
        var completedCount = completed.Sum();
        versions.ShouldBe(completedCount + postedBatches + drainedBatches);
        output.WriteLine($"{editors} editors / {workers} workers / 3 plans / paced={paced}: {watch.Elapsed.TotalSeconds:F2}s, {completedCount} user commits ({completedCount / watch.Elapsed.TotalSeconds:F1}/s), {conflicts} user conflicts ({100d * conflicts / (completedCount + conflicts):F1}%), {logs.LateConflicts} logged CAS conflicts across all peers; {batches} worker polls / {postedBatches} commits during load; {reads} register reads; {pending} templates still due at load end, drained in {drain.Elapsed.TotalSeconds:F2}s.");
        output.WriteLine($"User completion latency p50={Percentile(latency, 0.5):F1}ms p95={Percentile(latency, 0.95):F1}ms max={latency.Max():F1}ms; attempt p95={Percentile(attempts, 0.95):F1}ms; per-editor completed=[{string.Join(",", completed)}]. All 4,380 scheduled occurrences posted exactly once after draining; no unexpected error logs.");
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];
    }

    private static async Task AddDueTemplatesAsync(BudgetDatabase database, LedgerScaleFixture fixture, CancellationToken token)
    {
        var today = (await database.Queries.ViewAsync("owner-a", fixture.Id, token)).Catalog.Today;
        await using var context = await database.Factory.CreateDbContextAsync(token);
        for (var index = 0; index < Templates; index++)
        {
            var id = Guid.CreateVersion7();
            context.Add(new BudgetTransaction
            {
                Id = id,
                PlanId = fixture.Id,
                AccountId = fixture.Accounts[0].Id,
                Date = today.AddDays(1 - Occurrences),
                AnchorDate = today.AddDays(1 - Occurrences),
                Amount = -1,
                Payee = "Daily service",
                Repeat = RepeatFrequency.Daily,
                Sequence = fixture.Entries + index + 1,
            });
            context.Add(new BudgetSplit { Id = Guid.CreateVersion7(), PlanId = fixture.Id, TransactionId = id, CategoryId = fixture.Categories[^1].Id, Amount = -1 });
        }
        await context.SaveChangesAsync(token);
        await context.Set<BudgetPlan>().Where(item => item.Id == fixture.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.NextSequence, fixture.Entries + Templates), token);
    }

    private sealed class ErrorLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<byte> _lateConflicts = new();
        public int LateConflicts => _lateConflicts.Count;
        public ConcurrentQueue<string> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => new ErrorLogger(this, categoryName);
        public void Dispose() { }
        private sealed class ErrorLogger(ErrorLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) { return; }
                if (exception is DbUpdateConcurrencyException) { owner._lateConflicts.Enqueue(0); return; }
                owner.Errors.Enqueue($"{category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
