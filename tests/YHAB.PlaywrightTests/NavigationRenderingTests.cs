using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
[Collection<BrowserAppHostDefinition>]
public sealed class NavigationRenderingTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "BrowserSmoke")]
    public async Task WorkspaceRemountPaintsImmediatelyAndSlowNavigationShowsProgressAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        await using var builder = await TestAppHost.CreateAsync(timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("yhab", timeout.Token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = app.GetEndpoint("yhab", "https").ToString(),
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1280, Height = 960 },
            // Enhanced navigations are fetch requests forwarded by the worker;
            // blocking it lets this test throttle the real request.
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GetByRole(AriaRole.Link, new() { Name = "Open your plans" }).ClickAsync();
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("Navigation probe");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var planPath = new Uri(page.Url).AbsolutePath;
            var origin = await page.EvaluateAsync<double>("performance.timeOrigin");

            await page.EvaluateAsync("""
                () => {
                    window.yhabSawLoading = false;
                    new MutationObserver(() => {
                        if (document.querySelector('#main-content')?.textContent.includes('Opening your plan')) {
                            window.yhabSawLoading = true;
                        }
                    }).observe(document, { subtree: true, childList: true, characterData: true });
                }
                """);

            await page.Locator(".plan-navigation").GetByRole(AriaRole.Link, new() { Name = "Reflect", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Reflect", Exact = true }).WaitForAsync();
            await page.Locator($".plan-navigation a[href='{planPath}']").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Plan your money", Exact = true }).WaitForAsync();
            // The heading is already in the prerendered response. Wait for the returned
            // workspace to become interactive and finish its refresh before judging the
            // transition, otherwise this check can pass on the static HTML.
            await page.Locator(".workspace[data-interactive='true'][aria-busy='false']").WaitForAsync();

            (await page.EvaluateAsync<bool>("window.yhabSawLoading")).ShouldBeFalse("a remounted workspace must paint its cached state before the fresh read");
            (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);

            await page.Locator(".plan-navigation").GetByRole(AriaRole.Link, new() { Name = "Reflect", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Reflect", Exact = true }).WaitForAsync();
            var reportOrigin = await page.EvaluateAsync<double>("performance.timeOrigin");
            await page.Locator("#report-from").FillAsync("2026-01-01");
            await page.Locator("#report-to").FillAsync("2026-12-31");
            await page.GetByRole(AriaRole.Button, new() { Name = "Update reports", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("from=2026-01-01", StringComparison.Ordinal));
            (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(reportOrigin);
            await page.GetByRole(AriaRole.Heading, new() { Name = "Reflect", Exact = true }).WaitForAsync();

            await context.RouteAsync($"**{planPath}/settings", async route =>
            {
                // Inject latency so the indicator's delayed appearance is observable.
                await Task.Delay(TimeSpan.FromMilliseconds(800), timeout.Token);
                await route.ContinueAsync();
            });
            try
            {
                var navigation = page.Locator(".plan-navigation").GetByRole(AriaRole.Link, new() { Name = "Plan settings", Exact = true }).ClickAsync();
                await page.Locator("#navigation-progress[data-active='true']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 5000 });
                await navigation;
                await page.GetByRole(AriaRole.Heading, new() { Name = "Plan settings", Exact = true }).WaitForAsync();
                await page.WaitForFunctionAsync("() => !document.getElementById('navigation-progress')?.hasAttribute('data-active')");
            }
            finally
            {
                await context.UnrouteAsync($"**{planPath}/settings");
            }

            errors.ShouldBeEmpty();
        }
        finally
        {
            foreach (var error in errors)
            {
                output.WriteLine(error);
            }

            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"navigation-rendering-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    [Fact]
    [Trait("Category", "BrowserSmoke")]
    public async Task ColdStartSucceedsWhenThePlanIsTooLargeToPersistAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        await using var builder = await TestAppHost.CreateAsync(timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("yhab", timeout.Token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = app.GetEndpoint("yhab", "https").ToString(),
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1280, Height = 960 },
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GetByRole(AriaRole.Link, new() { Name = "Open your plans" }).ClickAsync();
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("Large notes plan");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var planPath = new Uri(page.Url).AbsolutePath;

            // Five 4,000-character notes are duplicated by the catalog and the month,
            // which by itself exceeds the 32 KiB circuit-start message. The renderer
            // must drop the persisted snapshot and still start.
            var notes = new string('n', 4000);
            var plan = await ReadPlanAsync(page, planPath);
            var token = await ReadTokenAsync(page);
            foreach (var category in plan.Categories.Take(5))
            {
                await using var saved = await page.APIRequest.PutAsync($"/api{planPath}/categories/{category.Id}", new()
                {
                    Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = token, ["Content-Type"] = "application/json" },
                    Data = JsonSerializer.Serialize(new SaveCategory(plan.Version, category with { Notes = notes }), JsonSerializerOptions.Web),
                });
                saved.Status.ShouldBe(200, await saved.TextAsync());
                plan = await ReadPlanAsync(page, planPath);
            }

            page.PageError += (_, error) => errors.Enqueue(error);
            await page.GotoAsync(planPath);
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync(new() { Timeout = 60_000 });
            await page.GetByRole(AriaRole.Heading, new() { Name = "Plan your money", Exact = true }).WaitForAsync();

            errors.ShouldBeEmpty();
        }
        finally
        {
            foreach (var error in errors)
            {
                output.WriteLine(error);
            }

            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"large-plan-cold-start-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    private static async Task<PlanSnapshot> ReadPlanAsync(IPage page, string planPath)
    {
        await using var response = await page.APIRequest.GetAsync($"/api{planPath}");
        response.Status.ShouldBe(200);
        return JsonSerializer.Deserialize<PlanSnapshot>(await response.TextAsync(), JsonSerializerOptions.Web).ShouldNotBeNull();
    }

    private static async Task<string> ReadTokenAsync(IPage page)
    {
        await using var response = await page.APIRequest.GetAsync("/api/plans/token");
        response.Status.ShouldBe(200);
        return (await response.JsonAsync()).ShouldNotBeNull().GetProperty("token").GetString()!;
    }
}
