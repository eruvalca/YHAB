using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class NavigationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task FluentNavigationPreservesDocumentAndThemeChoice(int width)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var builder = await TestAppHost.CreateAsync(timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("yhab", timeout.Token);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        // The isolated local AppHost uses the development HTTPS certificate.
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = app.GetEndpoint("yhab", "https").ToString(),
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = width, Height = 900 },
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        try
        {
            await VerifyNavigationAsync(page, width);
        }
        finally
        {
            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"navigation-{width}-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    private static async Task VerifyNavigationAsync(IPage page, int width)
    {
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        await page.GotoAsync("/");
        await page.Locator(".welcome h1").WaitForAsync();
        await page.WaitForFunctionAsync("() => customElements.get('fluent-button') && typeof Blazor !== 'undefined'");
        var origin = await page.EvaluateAsync<double>("performance.timeOrigin");

        await OpenNavigationAsync(page, width);
        var register = page.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true });
        await register.Locator("svg").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        (await register.Locator("svg").CountAsync()).ShouldBeGreaterThan(0);
        await register.ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Register", Exact = true }).WaitForAsync();
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);
        await AssertDrawerClosedAsync(page, width);

        await OpenNavigationAsync(page, width);
        await page.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true }).ClickAsync();
        await page.Locator(".welcome h1").WaitForAsync();
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);
        await AssertDrawerClosedAsync(page, width);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        await page.Locator("[data-theme-select]").SelectOptionAsync("dark");
        await page.WaitForFunctionAsync("document.body.dataset.theme === 'dark'");
        await VerifyThemeAfterNavigationAsync(page, width, "Register", "dark");
        (await page.Locator("button[type='submit']").First.EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth")).ShouldBe("1px");
        await VerifyThemeAfterNavigationAsync(page, width, "Home", "dark");
        await page.ReloadAsync();
        await page.WaitForFunctionAsync("document.body.dataset.theme === 'dark'");
        await page.Locator("[data-theme-select]").SelectOptionAsync("light");
        await page.WaitForFunctionAsync("document.body.dataset.theme !== 'dark'");
        await VerifyThemeAfterNavigationAsync(page, width, "Register", "light");
        await page.Locator("[data-theme-select]").SelectOptionAsync("system");
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await page.WaitForFunctionAsync("document.body.dataset.theme === 'dark'");
        await VerifyThemeAfterNavigationAsync(page, width, "Home", "dark");
        (await page.Locator("[data-theme-select]").InputValueAsync()).ShouldBe("system");
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await page.WaitForFunctionAsync("document.body.dataset.theme !== 'dark'");
        errors.ShouldBeEmpty();
    }

    private static async Task VerifyThemeAfterNavigationAsync(IPage page, int width, string destination, string theme)
    {
        var origin = await page.EvaluateAsync<double>("performance.timeOrigin");
        await OpenNavigationAsync(page, width);
        await page.GetByRole(AriaRole.Link, new() { Name = destination, Exact = true }).ClickAsync();
        await page.Locator(string.Equals(destination, "Home", StringComparison.Ordinal) ? ".welcome h1" : ".account-form").WaitForAsync();
        await page.WaitForFunctionAsync("expected => getComputedStyle(document.body).colorScheme === expected", theme);
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);
        await AssertDrawerClosedAsync(page, width);
    }

    private static async Task OpenNavigationAsync(IPage page, int width)
    {
        if (width < 768)
        {
            await page.Locator("#site-menu").ClickAsync();
            await page.Locator("fluent-drawer[hamburger] dialog").WaitForAsync();
        }
    }

    private static async Task AssertDrawerClosedAsync(IPage page, int width)
    {
        if (width < 768)
        {
            // The custom-element host has no layout box; inspect its actual shadow dialog.
            var drawer = page.Locator("fluent-drawer[hamburger] dialog");
            await drawer.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
            (await drawer.IsVisibleAsync()).ShouldBeFalse();
        }
    }
}
