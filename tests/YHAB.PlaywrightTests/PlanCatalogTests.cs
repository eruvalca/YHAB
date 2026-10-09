using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PlanCatalogTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task InlineCatalogEditingOrderingAndTargetsPersistAsync(int width)
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
            ViewportSize = new() { Width = width, Height = 960 },
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GotoAsync("/plans");
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("Comfortable planning");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var path = new Uri(page.Url).AbsolutePath;
            await SeedTargetsAsync(page, path);
            await page.ReloadAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            await VerifyDensityAsync(page, width);
            await CreateAndRenameAsync(page);
            var plan = await ReadAsync(page, path);
            var category = plan.Categories.Single(item => string.Equals(item.Name, "Adventure fund", StringComparison.Ordinal));
            var source = plan.Groups.Single(item => string.Equals(item.Name, "Dreams", StringComparison.Ordinal));
            var destination = plan.Groups.First(item => item.Id != source.Id);
            var anchor = plan.Categories.First(item => item.GroupId == destination.Id);
            var row = page.Locator($"tr[data-category-id='{category.Id}']");
            if (width >= 1000)
            {
                foreach (var group in plan.Groups.Where(item => item.Id != source.Id && item.Id != destination.Id))
                {
                    await page.GetByRole(AriaRole.Button, new() { Name = $"Collapse {group.Name}", Exact = true }).ClickAsync();
                }
                await row.Locator(".row-move").DragToAsync(page.Locator($"tr[data-category-id='{anchor.Id}']"));
                await page.Locator($"tbody[data-group-id='{destination.Id}'] tr[data-category-id='{category.Id}']").WaitForAsync();
                await page.Locator($"tbody[data-group-id='{source.Id}'] .group-row .row-move").DragToAsync(page.Locator($"tbody[data-group-id='{destination.Id}'] .group-row"));
                await page.WaitForFunctionAsync("id => document.querySelector('.budget-table tbody[data-group-id]')?.dataset.groupId === id", source.Id.ToString());
            }
            else
            {
                await row.Locator(".row-move").ClickAsync();
                await page.GetByRole(AriaRole.Combobox, new() { Name = "Move to group", Exact = true }).ClickAsync();
                await page.Locator($"fluent-option[text='{destination.Name}']").ClickAsync();
                await page.Locator($"tbody[data-group-id='{destination.Id}'] tr[data-category-id='{category.Id}']").WaitForAsync();
            }
            await row.Locator(".category-link").ClickAsync();
            await page.Locator(".category-details h3").Filter(new() { HasText = "Target" }).WaitForAsync();
            await page.Locator(".category-details fluent-checkbox").First.ClickAsync();
            await Input(page, "Target amount").FillAsync("250");
            await Input(page, "Target amount").PressAsync("Tab");
            await Button(page, "Save category").ClickAsync();
            await row.Locator(".target-caption").Filter(new() { HasText = "$250.00 more needed" }).WaitForAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"plan-target-{width}.png") });
            await page.ReloadAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            plan = await ReadAsync(page, path);
            var saved = plan.Categories.Single(item => item.Id == category.Id);
            saved.GroupId.ShouldBe(destination.Id);
            saved.Target.ShouldNotBeNull().Amount.ShouldBe(250);
            if (width >= 1000)
            {
                plan.Categories.Where(item => item.GroupId == destination.Id).OrderBy(item => item.SortOrder).First().Id.ShouldBe(category.Id);
                plan.Groups.OrderBy(item => item.SortOrder).First().Id.ShouldBe(source.Id);
            }
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
            await page.Locator("[data-theme-select]").SelectOptionAsync("dark");
            await page.WaitForFunctionAsync("document.body.dataset.theme === 'dark'");
            await page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"plan-compact-{width}.png") });
            errors.ShouldBeEmpty();
        }
        finally
        {
            await BrowserArtifacts.CaptureAsync(page, context, Path.Combine(AppContext.BaseDirectory, "TestResults", $"plan-catalog-{width}"), output.WriteLine);
        }
    }

    private static async Task CreateAndRenameAsync(IPage page)
    {
        await Button(page, "Category group").ClickAsync();
        await Input(page, "New group name").FillAsync("Future");
        await Button(page, "Save").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Rename Future", Exact = true }).ClickAsync();
        await Input(page, "Group name").FillAsync("Dreams");
        await Input(page, "Group name").PressAsync("Enter");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add category to Dreams", Exact = true }).ClickAsync();
        await Input(page, "New category name").FillAsync("Holiday");
        await Button(page, "Save").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Rename Holiday", Exact = true }).ClickAsync();
        await Input(page, "Category name").FillAsync("Adventure fund");
        await Input(page, "Category name").PressAsync("Enter");
        await page.GetByRole(AriaRole.Button, new() { Name = "Adventure fund", Exact = true }).WaitForAsync();
    }

    private static async Task VerifyDensityAsync(IPage page, int width)
    {
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        if (width >= 1000)
        {
            (await page.Locator("#desktop-navigation").IsVisibleAsync()).ShouldBeFalse();
            var first = (await page.Locator(".category-row").First.BoundingBoxAsync()).ShouldNotBeNull();
            first.Y.ShouldBeLessThan(320);
            first.Height.ShouldBeInRange(36, 64);
            var visible = await page.Locator(".category-row").EvaluateAllAsync<int>("rows => rows.filter(row => row.getBoundingClientRect().bottom <= innerHeight).length");
            visible.ShouldBeGreaterThanOrEqualTo(10);
        }
        else
        {
            (await page.Locator(".plan-drawer").GetAttributeAsync("open")).ShouldBeNull();
            await page.Locator(".plan-drawer > summary").ClickAsync();
            (await page.GetByRole(AriaRole.Link, new() { Name = "Plan your money", Exact = true }).IsVisibleAsync()).ShouldBeTrue();
            await page.Locator(".plan-drawer > summary").ClickAsync();
        }
    }

    private static async Task SeedTargetsAsync(IPage page, string path)
    {
        var plan = await ReadAsync(page, path);
        await using var response = await page.APIRequest.GetAsync("/api/plans/token");
        var token = (await response.JsonAsync()).ShouldNotBeNull().GetProperty("token").GetString()!;
        foreach (var category in plan.Categories)
        {
            var target = new TargetData(TargetKind.Refill, TargetCadence.Monthly, 100, BudgetFacts.Month(plan.Today), null, 0, DayOfWeek.Friday);
            await using var saved = await page.APIRequest.PutAsync($"/api{path}/categories/{category.Id}", new()
            {
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = token, ["Content-Type"] = "application/json" },
                Data = JsonSerializer.Serialize(new SaveCategory(plan.Version, category with { Target = target }), JsonSerializerOptions.Web),
            });
            saved.Status.ShouldBe(200, await saved.TextAsync());
            plan = await ReadAsync(page, path);
        }
    }

    private static async Task<PlanSnapshot> ReadAsync(IPage page, string path)
    {
        await using var response = await page.APIRequest.GetAsync($"/api{path}");
        response.Status.ShouldBe(200);
        return JsonSerializer.Deserialize<PlanSnapshot>(await response.TextAsync(), JsonSerializerOptions.Web).ShouldNotBeNull();
    }

    private static ILocator Input(IPage page, string label) => page.Locator("fluent-field")
        .Filter(new() { Has = page.GetByText(label, new() { Exact = true }) }).Locator("input");
    private static ILocator Button(IPage page, string label) => page.Locator("fluent-button")
        .Filter(new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(label)}\\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)) });
}
