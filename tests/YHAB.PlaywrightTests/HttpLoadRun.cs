using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using Shouldly;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.PlaywrightTests;

internal static class HttpLoadRun
{
    public static async Task ExecuteAsync(IAPIRequestContext request, IReadOnlyList<IAPIRequestContext> replicas,
        IReadOnlyList<HttpLoadFixture> fixtures, string csrf, WebProcessMeasurements processes,
        string memoryPath, int seconds, Action<string> write, CancellationToken token)
    {
        var completed = new int[8];
        var conflicts = new int[8];
        var refreshConflicts = new int[8];
        var refreshExhausted = new int[8];
        var refreshRecovered = new int[8];
        var latency = new ConcurrentBag<double>();
        var acceptedLatency = new ConcurrentBag<double>();
        var workflowLatency = new ConcurrentBag<double>();
        var displayedLatency = new ConcurrentBag<double>();
        var receipts = new ConcurrentBag<(Guid PlanId, Guid Receipt)>();
        var reads = 0;
        var caughtUpAt = -1d;
        var timer = Stopwatch.StartNew();
        async Task EditAsync(int editor)
        {
            var fixture = fixtures[editor % fixtures.Count];
            var replica = replicas[editor % replicas.Count];
            // Spread the offered load across a half-second interval instead of
            // synchronizing every writer into a burst against one revision.
            await Task.Delay(TimeSpan.FromMilliseconds(editor * 62.5), token);
            // A rejected edit is presented as a conflict, then the next paced
            // attempt refreshes the revision. There is no retry-until-success loop.
            using var cadence = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                token.ThrowIfCancellationRequested();
                var elapsed = Stopwatch.StartNew();
                var version = await HttpLoadFixture.ReadAsync<long>(replica, $"{fixture.Path}/revision");
                var change = new AssignMoney(version, fixture.Categories[editor], BudgetFacts.Month(fixture.Today), completed[editor] + 2);
                var response = await replica.PutAsync($"{fixture.Path}/assignments", new()
                {
                    Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = csrf, ["Content-Type"] = "application/json" },
                    Data = JsonSerializer.Serialize(change, JsonSerializerOptions.Web),
                    Timeout = 15000,
                });
                var saved = false;
                try
                {
                    if (response.Status == 409) { conflicts[editor]++; }
                    else
                    {
                        response.Status.ShouldBe(200, await response.TextAsync());
                        (await response.JsonAsync()).ShouldNotBeNull().GetProperty("version").GetInt64().ShouldBe(version + 1);
                        completed[editor]++;
                        receipts.Add((fixture.Id, change.OperationId));
                        acceptedLatency.Add(elapsed.Elapsed.TotalMilliseconds);
                        saved = true;
                    }
                    latency.Add(elapsed.Elapsed.TotalMilliseconds);
                }
                finally { await response.DisposeAsync(); }
                if (saved)
                {
                    var refresh = await fixture.RefreshMonthAsync(replica, editor, completed[editor] + 1);
                    refreshConflicts[editor] += refresh.Conflicts;
                    if (refresh.Loaded)
                    {
                        displayedLatency.Add(elapsed.Elapsed.TotalMilliseconds);
                        if (refresh.Conflicts > 0) { refreshRecovered[editor]++; }
                    }
                    else { refreshExhausted[editor]++; }
                    workflowLatency.Add(elapsed.Elapsed.TotalMilliseconds);
                }
                await cadence.WaitForNextTickAsync(token);
            }
        }
        async Task ReadAsync()
        {
            var query = Uri.EscapeDataString(JsonSerializer.Serialize(new RegisterQuery(), JsonSerializerOptions.Web));
            using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                token.ThrowIfCancellationRequested();
                var fixture = fixtures[reads % fixtures.Count];
                var page = await HttpLoadFixture.ReadAsync<RegisterPage>(request, $"{fixture.Path}/register?query={query}");
                page.Rows.Count.ShouldBe(50);
                page.Rows.Select(item => item.Transaction.Id).Distinct().Count().ShouldBe(50);
                page.Total.ShouldBeInRange(100000, 100730);
                reads++;
                await cadence.WaitForNextTickAsync(token);
            }
        }
        async Task MeasureAsync()
        {
            using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(10));
            do
            {
                processes.Capture(timer.Elapsed.TotalSeconds, write);
                await processes.SaveAsync(memoryPath, token);
                var due = false;
                foreach (var fixture in fixtures)
                {
                    var view = await HttpLoadFixture.ReadAsync<PlanView>(request, $"{fixture.Path}/view");
                    due |= view.HasDueRecurring;
                }
                if (!due && caughtUpAt < 0) { caughtUpAt = timer.Elapsed.TotalSeconds; }
                var progress = new { seconds = timer.Elapsed.TotalSeconds, plans = fixtures.Count, completed, conflicts, refreshConflicts, refreshRecovered, refreshExhausted, reads, caughtUpAt };
                await File.WriteAllTextAsync(Path.ChangeExtension(memoryPath, "progress.json"), JsonSerializer.Serialize(progress, JsonSerializerOptions.Web), token);
                await cadence.WaitForNextTickAsync(token);
            } while (timer.Elapsed.TotalSeconds < seconds);
        }
        processes.Capture(0, write);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(EditAsync).Append(ReadAsync()).Append(MeasureAsync()));
        processes.Capture(timer.Elapsed.TotalSeconds, write);
        completed.ShouldAllBe(count => count > 0);
        reads.ShouldBeGreaterThan(0);
        caughtUpAt.ShouldBeInRange(0, seconds);
        for (var index = 0; index < fixtures.Count; index++)
        {
            var fixture = fixtures[index];
            var assignments = completed.Select((count, editor) => editor % fixtures.Count == index ? count : 0).ToArray();
            await fixture.VerifyAsync(request, receipts.Where(item => item.PlanId == fixture.Id).Select(item => item.Receipt).ToArray(), assignments, token);
        }
        var sorted = latency.Order().ToArray();
        double Percentile(double quantile) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * quantile) - 1)];
        var accepted = acceptedLatency.Order().ToArray();
        var workflow = workflowLatency.Order().ToArray();
        var displayed = displayedLatency.Order().ToArray();
        displayed.ShouldNotBeEmpty();
        (displayed.Length + refreshExhausted.Sum()).ShouldBe(receipts.Count);
        write($"HTTP load: {timer.Elapsed.TotalSeconds:F1}s; attempts={sorted.Length}; completed={receipts.Count}; conflicts={conflicts.Sum()} ({100d * conflicts.Sum() / sorted.Length:F1}%); reads={reads}; p50/p95/p99/max={Percentile(.50):F1}/{Percentile(.95):F1}/{Percentile(.99):F1}/{sorted[^1]:F1}ms; worker catch-up observed at {caughtUpAt:F1}s.");
        write($"Accepted attempts: p95={accepted[(int)Math.Ceiling(accepted.Length * .95) - 1]:F1}ms; max={accepted[^1]:F1}ms. Neither latency series includes a user retry after a rejected edit.");
        write($"Save and balance/month refresh attempts: p95={workflow[(int)Math.Ceiling(workflow.Length * .95) - 1]:F1}ms; max={workflow[^1]:F1}ms; read conflicts={refreshConflicts.Sum()}; recovered={refreshRecovered.Sum()}; exhausted={refreshExhausted.Sum()}. Read recovery is bounded to three attempts; edits are never resent.");
        write($"Save through successfully refreshed balances: count={displayed.Length}; p95={displayed[(int)Math.Ceiling(displayed.Length * .95) - 1]:F1}ms; max={displayed[^1]:F1}ms. Includes all read recovery time, excludes exhausted refreshes and user retries of rejected writes.");
        write($"Per-editor commits=[{string.Join(",", completed)}]; conflicts=[{string.Join(",", conflicts)}]. Latency includes revision read and one HTTP mutation attempt, including conflicts.");
        // A measured idle interval, not a readiness wait. It shows whether process
        // memory returns after offered load without forcing a server GC.
        using var idle = new PeriodicTimer(TimeSpan.FromSeconds(10));
        for (var sample = 0; sample < 6; sample++)
        {
            await idle.WaitForNextTickAsync(token);
            processes.Capture(timer.Elapsed.TotalSeconds, write);
            await processes.SaveAsync(memoryPath, token);
        }
    }
}
