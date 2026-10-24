using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.Testing;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
[Collection<BrowserAppHostDefinition>]
public sealed class AccessibilityTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "BrowserSmoke")]
    public async Task CorePagesHaveOrderedHeadingsReadableControlsAndDarkContrastAsync()
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
        try
        {
            await page.GotoAsync("/");
            await page.Locator(".welcome h1").WaitForAsync();
            await page.Locator("[data-theme-select]").SelectOptionAsync("dark");
            await AssertAccessibilityAsync(page, "Home");

            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GotoAsync("/plans");
            await page.Locator(".page-intro h1").WaitForAsync();
            await AssertAccessibilityAsync(page, "Plans");

            await page.Locator("#plan-name").FillAsync("Accessible plan");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true'] .budget-summary h1").WaitForAsync();
            await AssertAccessibilityAsync(page, "Plan");

            await page.GotoAsync("/Account/Manage");
            await page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true }).WaitForAsync();
            await AssertAccessibilityAsync(page, "Manage");
        }
        finally
        {
            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"accessibility-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    private static async Task AssertAccessibilityAsync(IPage page, string pageName)
    {
        await page.WaitForFunctionAsync("document.body.dataset.theme === 'dark'");
        var result = await page.RunAxe(new AxeRunContext
        {
            Exclude = [new AxeSelector("fluent-button[disabled]")],
        }, new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "rule",
                Values = ["heading-order", "color-contrast"],
            },
        });
        var violations = result.Violations.Select(violation =>
            $"{violation.Id}: {string.Join(" | ", violation.Nodes.Select(node => $"{node.Target} ({node.Html})"))}").ToArray();
        violations.ShouldBeEmpty(pageName);

        var fontSizes = await page.EvaluateAsync<double[]>("""
            () => {
                const sizes = [];
                const visit = root => {
                    for (const control of root.querySelectorAll('input:not([type="hidden"]), select, textarea')) {
                        if (control.getClientRects().length > 0) {
                            sizes.push(parseFloat(getComputedStyle(control).fontSize));
                        }
                    }
                    for (const element of root.querySelectorAll('*')) {
                        if (element.shadowRoot) visit(element.shadowRoot);
                    }
                };
                visit(document);
                return sizes;
            }
            """);
        fontSizes.ShouldNotBeEmpty(pageName);
        fontSizes.Min().ShouldBeGreaterThanOrEqualTo(16, pageName);
    }
}
