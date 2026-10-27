using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
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
}
