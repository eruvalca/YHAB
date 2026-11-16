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
public sealed class PillContrastTests(ITestOutputHelper output)
{
    private const double MinimumTextContrast = 4.5;
    private const double MinimumBoundaryContrast = 3;
    private const string ReadPillScript = """
        pill => {
            const parse = value => {
                const match = /rgba?\(([^)]+)\)/.exec(value);
                if (!match) return null;
                const parts = match[1].split(/[,\s/]+/).filter(part => part.length > 0).map(Number);
                return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
            };
            // The row paints the state background on an ancestor, so resolve the
            // nearest opaque color actually behind the pill.
            const behind = start => {
                for (let node = start; node; node = node.parentElement) {
                    const color = parse(getComputedStyle(node).backgroundColor);
                    if (color && color.a >= 0.999) return color;
                }
                return parse(getComputedStyle(document.body).backgroundColor);
            };
            const style = getComputedStyle(pill);
            return JSON.stringify({
                tone: pill.dataset.tone,
                fill: parse(style.backgroundColor),
                text: parse(style.color),
                stroke: parse(style.borderTopColor),
                strokeWidth: parseFloat(style.borderTopWidth),
                backdrop: behind(pill.parentElement),
            });
        }
        """;
    private static readonly (string Category, string Tone)[] _tones =
    [
        ("Funded", "positive"),
        ("Needs funding", "attention"),
        ("Overspent", "negative"),
    ];

    [Fact]
    [Trait("Category", "BrowserSmoke")]
    public async Task AvailablePillTonesKeepContrastInBothThemesAndRowStatesAsync()
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
            ViewportSize = new() { Width = 1440, Height = 960 },
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            var planPath = await SeedPlanAsync(page);
            await page.GotoAsync(planPath);
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            await page.Locator(".budget-table .category-row").First.WaitForAsync();

            foreach (var theme in new[] { "light", "dark" })
            {
                await SetThemeAsync(page, theme);
                await VerifyThemeAsync(page, theme);
            }

            errors.ShouldBeEmpty();
        }
        finally
        {
            foreach (var error in errors)
            {
                output.WriteLine(error);
            }

            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"pill-contrast-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    private static async Task VerifyThemeAsync(IPage page, string theme)
    {
        await page.Mouse.MoveAsync(0, 0);
        foreach (var (category, tone) in _tones)
        {
            await VerifyPillAsync(page, category, tone, theme, "default");
        }

        foreach (var (category, tone) in _tones)
        {
            await Row(page, category).HoverAsync();
            await VerifyPillAsync(page, category, tone, theme, "hover");
        }

        await page.Mouse.MoveAsync(0, 0);
        foreach (var (category, tone) in _tones)
        {
            var row = Row(page, category);
            var id = (await row.GetAttributeAsync("data-category-id")).ShouldNotBeNull();
            await row.Locator(".category-link").ClickAsync();
            await page.Locator($"[data-category-id='{id}'][data-selected='true']").WaitForAsync();
            // Selecting leaves the pointer on the row; move it away so the
            // measured background is the selected fill, not hover over selection.
            await page.Mouse.MoveAsync(0, 0);
            await VerifyPillAsync(page, category, tone, theme, "selected");
            await BudgetWorkflowTests.Button(page, "Close").ClickAsync();
            await page.Locator($"[data-category-id='{id}'][data-selected='false']").WaitForAsync();
        }
    }

    private static async Task VerifyPillAsync(IPage page, string category, string expectedTone, string theme, string state)
    {
        var pill = Row(page, category).Locator(".available-pill");
        var json = await pill.EvaluateAsync<string>(ReadPillScript);
        var style = JsonSerializer.Deserialize<PillStyle>(json, JsonSerializerOptions.Web).ShouldNotBeNull();
        var label = $"{theme} theme, {state} row, {expectedTone} pill ({category})";

        style.Tone.ShouldBe(expectedTone, label);
        style.Fill.A.ShouldBe(1d, $"{label}: the fill must be an opaque palette color");
        style.Text.A.ShouldBe(1d, $"{label}: the amount must not inherit a translucent color");

        // Guard that the rendered theme really changed, so a stuck light theme
        // cannot make the dark pass measure light colors.
        var backdropLuminance = Luminance(style.Backdrop);
        if (string.Equals(theme, "light", StringComparison.Ordinal))
        {
            backdropLuminance.ShouldBeGreaterThan(0.5, $"{label}: expected a light row backdrop, got luminance {backdropLuminance:F2}");
        }
        else
        {
            backdropLuminance.ShouldBeLessThan(0.2, $"{label}: expected a dark row backdrop, got luminance {backdropLuminance:F2}");
        }

        var textContrast = Contrast(style.Text, style.Fill);
        textContrast.ShouldBeGreaterThanOrEqualTo(MinimumTextContrast,
            $"{label}: {textContrast:F2}:1 amount-to-fill contrast");

        var fillContrast = Contrast(style.Fill, style.Backdrop);
        var strokeContrast = style.StrokeWidth >= 1 ? Contrast(style.Stroke, style.Backdrop) : 0;
        Math.Max(fillContrast, strokeContrast).ShouldBeGreaterThanOrEqualTo(MinimumBoundaryContrast,
            $"{label}: {fillContrast:F2}:1 fill and {strokeContrast:F2}:1 stroke against the row");
    }

    private static ILocator Row(IPage page, string category) => page.Locator(".budget-table .category-row")
        .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = category, Exact = true }) });

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        await page.Locator("[data-theme-select]").SelectOptionAsync(theme);
        // Fluent only marks the body for dark mode, so the app's resolved root
        // theme is the reliable signal that the change handler ran.
        await page.WaitForFunctionAsync("theme => document.documentElement.dataset.theme === theme", theme);
    }

    private static double Contrast(Rgba first, Rgba second)
    {
        var (lighter, darker) = (Luminance(first), Luminance(second));
        if (lighter < darker)
        {
            (lighter, darker) = (darker, lighter);
        }

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Rgba color)
    {
        static double Channel(double value)
        {
            var channel = value / 255;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static async Task<string> SeedPlanAsync(IPage page)
    {
        var request = page.APIRequest;
        var tokenResponse = await request.GetAsync("/api/plans/token");
        var token = (await tokenResponse.JsonAsync()).ShouldNotBeNull().GetProperty("token").GetString().ShouldNotBeNull();
        await tokenResponse.DisposeAsync();

        var created = await request.PostAsync("/api/plans/", new()
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = token },
            DataObject = new { name = "Pill contrast plan", starterCategories = false },
        });
        created.Status.ShouldBe(201, await created.TextAsync());
        var planId = (await created.JsonAsync()).ShouldNotBeNull().GetGuid();
        await created.DisposeAsync();

        var read = await request.GetAsync($"/api/plans/{planId}");
        read.Status.ShouldBe(200);
        var plan = JsonSerializer.Deserialize<PlanSnapshot>(await read.TextAsync(), JsonSerializerOptions.Web).ShouldNotBeNull();
        await read.DisposeAsync();
        var month = BudgetFacts.Month(plan.Today);

        // One row per tone: funded money, an unfunded target, and spending past
        // the assignment. These are the states the pill colors must signal.
        var api = new PlanApi(request, token, planId);
        var group = new GroupData(Guid.CreateVersion7(), "Essentials", 0);
        var account = new AccountData(Guid.CreateVersion7(), "Checking", AccountKind.Checking, 1000m, month, false, "");
        var funded = new CategoryData(Guid.CreateVersion7(), group.Id, "Funded", "", 0, false, null, null);
        var needsFunding = new CategoryData(Guid.CreateVersion7(), group.Id, "Needs funding", "", 1, false, null,
            new TargetData(TargetKind.Refill, TargetCadence.Monthly, 75m, month, null));
        var overspent = new CategoryData(Guid.CreateVersion7(), group.Id, "Overspent", "", 2, false, null, null);
        var expense = new TransactionData(Guid.CreateVersion7(), account.Id, month, "Market", "", -75m, null, "",
            [new SplitData(Guid.CreateVersion7(), overspent.Id, -75m, "")]);

        await api.PutAsync($"groups/{group.Id}", version => new SaveGroup(version, group));
        await api.PutAsync($"accounts/{account.Id}", version => new SaveAccount(version, account));
        await api.PutAsync($"categories/{funded.Id}", version => new SaveCategory(version, funded));
        await api.PutAsync($"categories/{needsFunding.Id}", version => new SaveCategory(version, needsFunding));
        await api.PutAsync($"categories/{overspent.Id}", version => new SaveCategory(version, overspent));
        await api.PutAsync("assignments", version => new AssignMoney(version, funded.Id, month, 100m));
        await api.PutAsync("assignments", version => new AssignMoney(version, overspent.Id, month, 50m));
        await api.PutAsync($"transactions/{expense.Id}", version => new SaveTransaction(version, expense));

        return $"/plans/{planId}";
    }

    private sealed class PlanApi(IAPIRequestContext request, string token, Guid planId)
    {
        private long _version;

        public async Task PutAsync(string resource, Func<long, PlanCommand> create)
        {
            var command = create(_version);
            var response = await request.PutAsync($"/api/plans/{planId}/{resource}", new()
            {
                Headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["X-CSRF-TOKEN"] = token,
                    ["Content-Type"] = "application/json",
                },
                Data = JsonSerializer.Serialize(command, command.GetType(), JsonSerializerOptions.Web),
            });
            response.Status.ShouldBe(200, await response.TextAsync());
            _version = (await response.JsonAsync()).ShouldNotBeNull().GetProperty("version").GetInt64();
            await response.DisposeAsync();
        }
    }

    private sealed record Rgba(double R, double G, double B, double A);

    private sealed record PillStyle(string Tone, Rgba Fill, Rgba Text, Rgba Stroke, double StrokeWidth, Rgba Backdrop);
}
