using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class InspectorRetentionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DisconnectingDiagnosticsPreservesTheLivePlanAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cycles = int.Parse(Environment.GetEnvironmentVariable("YHAB_BROWSER_CYCLES") ?? "120", CultureInfo.InvariantCulture);
        cycles.ShouldBeInRange(120, 1800);
        timeout.CancelAfter(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(cycles - 120));
        await using var builder = await TestAppHost.CreateAsync(timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("yhab", timeout.Token);
        var origin = app.GetEndpoint("yhab", "https");
        using var playwright = await Playwright.CreateAsync();
        await using var owned = new InspectableBrowser();
        await owned.StartAsync(playwright, timeout.Token);
        var browser = owned.Browser;
        var context = browser.Contexts.Single();
        var page = context.Pages.Single();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        await page.SetViewportSizeAsync(1440, 960);
        await BudgetWorkflowTests.RegisterAndLoginAsync(page, origin);
        await using var request = await playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = origin.AbsoluteUri,
            StorageState = await context.StorageStateAsync(),
            IgnoreHTTPSErrors = true,
        });
        var csrf = (await HttpLoadFixture.ReadAsync<JsonElement>(request, "/api/plans/token")).GetProperty("token").GetString().ShouldNotBeNull();
        var connection = (await app.GetConnectionStringAsync("yhabdb", timeout.Token)).ShouldNotBeNull();
        var fixture = await HttpLoadFixture.CreateAsync(request, csrf, connection, timeout.Token, includeRecurring: false, assigned: 0);
        var path = $"/plans/{fixture.Id}";
        await page.GotoAsync(new Uri(origin, $"{path}/accounts").AbsoluteUri);
        await page.WaitForFunctionAsync("() => Object.keys(localStorage).some(key => key.startsWith('blazor-resource-hash:'))", null, new() { Timeout = 60000 });
        await page.ReloadAsync();
        await page.Locator(".workspace[data-renderer='WebAssembly'][data-interactive='true']").WaitForAsync();
        await page.Locator(".pagination > span").Filter(new() { HasText = "Page 1 · 100000 transactions" }).WaitForAsync();
        await BrowserEditingSession.RunAsync(page, context, browser, path, "Editor 1", cycles, output.WriteLine, timeout.Token);

        var timeOrigin = await page.EvaluateAsync<double>("performance.timeOrigin");
        await page.EvaluateAsync("() => { globalThis.yhabControlDocument = document; globalThis.yhabControlRuntime = getDotnetRuntime(0); globalThis.yhabControlWorkspace = document.querySelector('.workspace'); }");
        var renderers = await RendererIdsAsync(browser);
        var before = await BrowserMemory.CaptureAsync(browser, context, page, "control before disconnect", output.WriteLine);
        await BrowserMemory.SnapshotAsync(context, page, "control-before-disconnect");
        await owned.ReconnectAsync(playwright);
        browser = owned.Browser;
        context = browser.Contexts.Single();
        page = context.Pages.Single();
        page.PageError += (_, error) => errors.Enqueue(error);
        (await RendererIdsAsync(browser)).ShouldBe(renderers);
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(timeOrigin);
        (await page.EvaluateAsync<bool>("yhabControlDocument === document && yhabControlRuntime === getDotnetRuntime(0) && yhabControlWorkspace === document.querySelector('.workspace')")).ShouldBeTrue();
        page.Url.ShouldBe(new Uri(origin, $"{path}/accounts").AbsoluteUri);
        (await page.Locator(".workspace").GetAttributeAsync("data-renderer")).ShouldBe("WebAssembly");
        (await page.Locator(".register-table tbody tr").CountAsync()).ShouldBe(50);
        var after = await BrowserMemory.CaptureAsync(browser, context, page, "control after reconnect", output.WriteLine);
        after.DomNodes.ShouldBeLessThan(before.DomNodes + 500);
        await BrowserMemory.SnapshotAsync(context, page, "control-after-reconnect");
        output.WriteLine($"Diagnostic reconnect preserved timeOrigin={timeOrigin}, renderer PIDs=[{string.Join(',', renderers)}], document, workspace and runtime identities. It did not reload or clear application state.");

        // Continue through real controls after reconnect and verify persistence
        // plus a literal independent cash total, not just a surviving screenshot.
        await page.GetByRole(AriaRole.Link, new() { Name = "Plan your money", Exact = true }).ClickAsync();
        var amount = page.GetByRole(AriaRole.Textbox, new() { Name = "Assign to Editor 1", Exact = true });
        await amount.FillAsync((cycles + 1).ToString(CultureInfo.InvariantCulture));
        await amount.PressAsync("Tab");
        await page.Locator(".budget-total").Filter(new() { Has = page.GetByText("Assigned", new() { Exact = true }) })
            .Locator("strong").Filter(new() { HasText = BudgetFacts.Money(cycles + 1) }).WaitForAsync();
        await page.Locator(".workspace-status").Filter(new() { HasText = "All changes saved" }).WaitForAsync();
        var view = await HttpLoadFixture.ReadAsync<PlanView>(request, $"{fixture.Path}/view");
        view.Balances.Sum(item => item.Working).ShouldBe(2_910_000);
        view.Catalog.Version.ShouldBe(cycles + 2);
        view.HasDueRecurring.ShouldBeFalse();
        (await fixture.RefreshMonthAsync(request, 0, cycles + 1)).ShouldBe((true, 0));
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(timeOrigin);
        errors.ShouldBeEmpty();
    }

    private static async Task<int[]> RendererIdsAsync(IBrowser browser)
    {
        var session = await browser.NewBrowserCDPSessionAsync();
        try
        {
            var result = (await session.SendAsync("SystemInfo.getProcessInfo").WaitAsync(TimeSpan.FromSeconds(30))).ShouldNotBeNull();
            return result.GetProperty("processInfo").EnumerateArray()
                .Where(item => string.Equals(item.GetProperty("type").GetString(), "renderer", StringComparison.Ordinal))
                .Select(item => checked((int)item.GetProperty("id").GetDouble())).Order().ToArray();
        }
        finally { await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
    }
}
