using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
[Collection<BrowserAppHostDefinition>]
public sealed class LayoutAndSharingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task InstallFooterPlanCardsAndShareCodeRemainUsableAsync(int width)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
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
            ViewportSize = new() { Width = width, Height = 900 },
        });
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GetByRole(AriaRole.Link, new() { Name = "Open your plans" }).ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Create a plan", Exact = true }).WaitForAsync();
            foreach (var theme in new[] { "light", "dark" })
            {
                await page.Locator("[data-theme-select]").SelectOptionAsync(theme);
                await page.WaitForFunctionAsync("theme => getComputedStyle(document.body).colorScheme === theme", theme);
                await VerifyPlansLayoutAsync(page);
                await VerifyFooterAsync(page);
                await page.ScreenshotAsync(new()
                {
                    Path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"plans-layout-{width}-{theme}.png"),
                    FullPage = true,
                });
            }

            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("A shared plan");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            await VerifyBudgetPhoneGeometryAsync(page, width);
            var settingsPath = new Uri(page.Url).AbsolutePath + "/settings";
            await page.GotoAsync(settingsPath);
            await page.GetByRole(AriaRole.Heading, new() { Name = "Plan settings", Exact = true }).WaitForAsync();
            var origin = await page.EvaluateAsync<double>("performance.timeOrigin");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create share code", Exact = true }).ClickAsync();
            await page.Locator(".share-code").WaitForAsync();
            var firstCode = await CopyAndVerifyAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create share code", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("previous => document.querySelector('.share-code')?.textContent !== previous", firstCode);
            (await CopyAndVerifyAsync(page)).ShouldNotBe(firstCode, StringComparer.Ordinal);
            (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);

            // A browser denial must leave the code visible and offer manual copying.
            await page.EvaluateAsync("""
                () => {
                    navigator.clipboard.writeText = async () => { throw new DOMException('Denied', 'NotAllowedError'); };
                }
                """);
            var copy = page.GetByRole(AriaRole.Button, new() { Name = "Copy code", Exact = true });
            await copy.FocusAsync();
            await copy.PressAsync("Enter");
            await page.Locator("[data-copy-share-code-status]").Filter(new() { HasText = "Could not copy the code." }).WaitForAsync();
            (await page.Locator(".share-code").InnerTextAsync()).ShouldNotBeNullOrWhiteSpace();
            (await copy.EvaluateAsync<bool>("element => document.activeElement === element")).ShouldBeTrue();
            (await copy.GetAttributeAsync("aria-disabled")).ShouldBeNull();
            await copy.PressAsync("Enter");
            await page.Locator("[data-copy-share-code-status]").Filter(new() { HasText = "Could not copy the code." }).WaitForAsync();
            (await copy.EvaluateAsync<bool>("element => document.activeElement === element")).ShouldBeTrue();
            await VerifyFooterAsync(page);
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"share-code-{width}.png"),
                FullPage = true,
            });
        }
        finally
        {
            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"layout-sharing-{width}-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    private static async Task VerifyBudgetPhoneGeometryAsync(IPage page, int width)
    {
        if (width >= 600)
        {
            return;
        }

        // The stacked phone layout must not need horizontal scrolling, and the
        // assignment field must sit fully inside the table's visible bounds.
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        var container = page.Locator(".budget-table-container");
        (await container.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1")).ShouldBeTrue();
        var bounds = (await container.BoundingBoxAsync()).ShouldNotBeNull();
        var amount = (await page.Locator(".budget-table .category-row fluent-text-input").First.BoundingBoxAsync()).ShouldNotBeNull();
        amount.X.ShouldBeGreaterThanOrEqualTo(bounds.X - 1);
        (amount.X + amount.Width).ShouldBeLessThanOrEqualTo(bounds.X + bounds.Width + 1);

        // Every compact control in the budget workspace reaches the 40px target.
        var targets = new[]
        {
            page.Locator(".workspace-toolbar fluent-button").First,
            page.Locator(".month-navigation fluent-button").First,
            page.Locator(".budget-summary fluent-button").First,
            page.Locator(".budget-table .group-add").First,
            page.Locator(".budget-table .group-toggle").First,
            page.Locator(".budget-table .rename-name").First,
            page.Locator(".budget-table .category-row .row-move").First,
            page.Locator(".budget-table .category-row fluent-text-input").First,
            page.Locator("[data-theme-select]"),
        };
        foreach (var target in targets)
        {
            (await target.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(40);
        }

        foreach (var arrow in await page.Locator(".workspace fluent-button[icon-only]").AllAsync())
        {
            (await arrow.BoundingBoxAsync()).ShouldNotBeNull().Width.ShouldBeGreaterThanOrEqualTo(40);
        }
    }

    private static async Task VerifyPlansLayoutAsync(IPage page)
    {
        var create = (await page.Locator(".surface").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Create a plan", Exact = true }) }).BoundingBoxAsync()).ShouldNotBeNull();
        var join = (await page.Locator(".surface").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Join a shared plan", Exact = true }) }).BoundingBoxAsync()).ShouldNotBeNull();
        (join.Y - create.Y - create.Height).ShouldBeGreaterThanOrEqualTo(24);
        join.X.ShouldBe(create.X, 1);
        join.Width.ShouldBe(create.Width, 1);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
    }

    private static async Task VerifyFooterAsync(IPage page)
    {
        var footer = page.Locator(".site-footer");
        (await footer.EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth")).ShouldBe("0px");
        (await footer.EvaluateAsync<float>("element => parseFloat(getComputedStyle(element).paddingBottom)")).ShouldBeGreaterThanOrEqualTo(24);
        var summary = page.Locator("[data-pwa-install-help] summary");
        await summary.ScrollIntoViewIfNeededAsync();
        var target = (await summary.BoundingBoxAsync()).ShouldNotBeNull();
        target.Height.ShouldBeGreaterThanOrEqualTo(44);
        var bounds = (await footer.BoundingBoxAsync()).ShouldNotBeNull();
        target.Y.ShouldBeGreaterThanOrEqualTo(bounds.Y + 16);
        (bounds.Y + bounds.Height - target.Y - target.Height).ShouldBeGreaterThanOrEqualTo(24);
        await summary.FocusAsync();
        await summary.PressAsync("Enter");
        await page.Locator("[data-pwa-install-help][open]").WaitForAsync();
        var expanded = (await footer.BoundingBoxAsync()).ShouldNotBeNull();
        expanded.Height.ShouldBeGreaterThan(bounds.Height);
        await summary.PressAsync("Enter");
        await page.Locator("[data-pwa-install-help][open]").WaitForAsync(new() { State = WaitForSelectorState.Detached });
    }

    private static async Task<string> CopyAndVerifyAsync(IPage page)
    {
        var code = await page.Locator(".share-code").InnerTextAsync();
        var copy = page.GetByRole(AriaRole.Button, new() { Name = "Copy code", Exact = true });
        (await copy.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
        await copy.EvaluateAsync("""
            button => {
                const writeText = navigator.clipboard.writeText.bind(navigator.clipboard);
                button.dataset.copyAttempts = '0';
                navigator.clipboard.writeText = async text => {
                    button.dataset.copyAttempts = String(Number(button.dataset.copyAttempts) + 1);
                    await new Promise(resolve => document.addEventListener('release-copy', resolve, { once: true }));
                    navigator.clipboard.writeText = writeText;
                    await writeText(text);
                };
            }
            """);
        await copy.FocusAsync();
        await copy.PressAsync("Enter");
        (await copy.GetAttributeAsync("aria-disabled")).ShouldBe("true");
        (await copy.EvaluateAsync<bool>("element => document.activeElement === element")).ShouldBeTrue();
        await copy.PressAsync("Enter");
        await copy.DispatchEventAsync("click");
        (await copy.GetAttributeAsync("data-copy-attempts")).ShouldBe("1");
        await page.EvaluateAsync("document.dispatchEvent(new Event('release-copy'))");
        await page.Locator("[data-copy-share-code-status]").Filter(new() { HasText = "Share code copied." }).WaitForAsync();
        (await copy.EvaluateAsync<bool>("element => document.activeElement === element")).ShouldBeTrue();
        (await copy.GetAttributeAsync("aria-disabled")).ShouldBeNull();
        (await page.EvaluateAsync<string>("navigator.clipboard.readText()")).ShouldBe(code);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        return code;
    }
}
