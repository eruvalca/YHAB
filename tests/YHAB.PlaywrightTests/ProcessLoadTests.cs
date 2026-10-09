using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
[Collection<BrowserAppHostDefinition>]
public sealed class ProcessLoadTests(ITestOutputHelper output)
{
    [Fact]
    public async Task IndependentWebProcessesPreserveMoneyDuringSustainedHttpLoadAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var seconds = int.Parse(Environment.GetEnvironmentVariable("YHAB_LOAD_SECONDS") ?? "180", CultureInfo.InvariantCulture);
        seconds.ShouldBeInRange(120, 1800);
        var planCount = int.Parse(Environment.GetEnvironmentVariable("YHAB_LOAD_PLANS") ?? "1", CultureInfo.InvariantCulture);
        (planCount is 1 or 8).ShouldBeTrue("measure one shared plan or eight independently edited plans");
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds + 240));
        await using var builder = await TestAppHost.CreateAsync(timeout.Token);
        var web = builder.Resources.OfType<ProjectResource>().Single(item => string.Equals(item.Name, "yhab", StringComparison.Ordinal));
        builder.CreateResourceBuilder(web).WithReplicas(2);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("yhab", timeout.Token);
        var configuration = typeof(ProcessLoadTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        using var processes = await WebProcessMeasurements.CreateAsync(app, web.Annotations.OfType<IProjectMetadata>().Single().ProjectPath, configuration, timeout.Token);
        var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"process-load-{Guid.NewGuid():N}");
        output.WriteLine($"Configuration={configuration}; offered load={seconds}s; plans={planCount}; two independent web processes; real 30-second workers; artifacts={artifacts}.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new() { BaseURL = app.GetEndpoint("yhab", "https").ToString(), IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        await BudgetWorkflowTests.RegisterAndLoginAsync(page);
        var csrf = (await HttpLoadFixture.ReadAsync<JsonElement>(page.APIRequest, "/api/plans/token")).GetProperty("token").GetString().ShouldNotBeNull();
        var connection = (await app.GetConnectionStringAsync("yhabdb", timeout.Token)).ShouldNotBeNull();
        var fixtures = new List<HttpLoadFixture>();
        for (var index = 0; index < planCount; index++)
        {
            fixtures.Add(await HttpLoadFixture.CreateAsync(page.APIRequest, csrf, connection, timeout.Token));
        }
        var state = await context.StorageStateAsync();
        await page.CloseAsync();
        var replicas = new List<IAPIRequestContext>();
        var memoryPath = Path.Combine(artifacts, "server-memory.json");
        try
        {
            foreach (var endpoint in processes.Endpoints)
            {
                var replica = await playwright.APIRequest.NewContextAsync(new() { BaseURL = endpoint.ToString(), StorageState = state, IgnoreHTTPSErrors = true });
                replicas.Add(replica);
                await HttpLoadFixture.ReadAsync<JsonElement>(replica, $"{fixtures[0].Path}/view");
                output.WriteLine($"Direct authenticated replica: {endpoint}.");
            }
            var opening = Stopwatch.StartNew();
            foreach (var fixture in fixtures)
            {
                // The real UI loads its month before enabling assignment inputs.
                // The helper uses the same three-read-attempt limit as Workspace.
                var refresh = await fixture.RefreshMonthAsync(replicas[0], 0, 1);
                refresh.Loaded.ShouldBeTrue("the initial month must be visible before editing starts");
            }
            output.WriteLine($"Initial month reads before editing: {opening.Elapsed.TotalMilliseconds:F1}ms across {fixtures.Count} plans.");
            await HttpLoadRun.ExecuteAsync(context.APIRequest, replicas, fixtures, csrf, processes, memoryPath, seconds, output.WriteLine, timeout.Token);
            var logs = app.Services.GetRequiredService<ResourceLoggerService>();
            foreach (var resource in processes.Resources)
            {
                var errors = new List<string>();
                await foreach (var batch in logs.GetAllAsync(resource).WithCancellation(timeout.Token))
                {
                    errors.AddRange(batch.Where(line => line.IsErrorMessage || line.Content.StartsWith("fail:", StringComparison.Ordinal)
                        || line.Content.StartsWith("crit:", StringComparison.Ordinal)).Select(line => line.Content));
                }
                errors.ShouldBeEmpty();
            }
        }
        finally
        {
            foreach (var replica in replicas) { await replica.DisposeAsync(); }
            await processes.SaveAsync(memoryPath, CancellationToken.None);
        }
    }
}
