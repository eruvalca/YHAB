using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BudgetWorkflowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task ManualPlanPurchaseUndoAndReportsAgreeAsync(int width)
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
            await RegisterAndLoginAsync(page);
            await CreatePlanAsync(page);
            await VerifyBudgetLayoutAsync(page, width);
            await VerifyApiGuardsAsync(page);
            await AddAccountAndAssignAsync(page);
            var planUrl = page.Url;
            await PurchaseAsync(page);
            await page.GotoAsync(planUrl);
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            await page.Locator(".hero-amount").Filter(new() { HasText = "$700.00" }).WaitForAsync();
            var groceries = page.Locator(".budget-table tr").Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Groceries", Exact = true }) });
            (await groceries.Locator(".available-pill").InnerTextAsync()).ShouldBe("$225.00");
            await page.GetByRole(AriaRole.Heading, new() { Name = "Plan your money", Exact = true }).ClickAsync();
            await page.Keyboard.PressAsync("Control+z");
            await groceries.Locator(".available-pill").Filter(new() { HasText = "$300.00" }).WaitForAsync();
            await Button(page, "Redo").ClickAsync();
            await groceries.Locator(".available-pill").Filter(new() { HasText = "$225.00" }).WaitForAsync();
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
            await page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"budget-{width}-light.png"), FullPage = true });
            await page.Locator("[data-theme-select]").SelectOptionAsync("dark");
            await page.WaitForFunctionAsync("document.body.dataset.theme === 'dark'");
            await VerifyBudgetLayoutAsync(page, width);
            await page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"budget-{width}-dark.png"), FullPage = true });
            await page.GetByRole(AriaRole.Link, new() { Name = "Reflect", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Reflect", Exact = true }).WaitForAsync();
            await page.WaitForFunctionAsync("document.body.dataset.theme === 'dark'");
            var spending = page.Locator(".spending-row").Filter(new() { HasText = "Groceries" });
            (await spending.Locator("strong").InnerTextAsync()).ShouldBe("$75.00");
            await ReconcileAsync(page);
            errors.ShouldBeEmpty();
        }
        finally
        {
            foreach (var error in errors)
            {
                output.WriteLine(error);
            }

            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"budget-{width}-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    internal static async Task RegisterAndLoginAsync(IPage page)
    {
        var anonymousPlans = await page.APIRequest.GetAsync("/api/plans");
        anonymousPlans.Status.ShouldBe(401);
        var email = $"budget-{Guid.NewGuid():N}@example.test";
        const string Password = "Local-test-Only!7926";
        await page.GotoAsync("/Account/Register");
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByLabel("Confirm Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Click here to confirm your account" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Confirm email", Exact = true }).WaitForAsync();
        await page.GotoAsync("/Account/Login");
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.Locator(".welcome h1").WaitForAsync();
    }

    private static async Task CreatePlanAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Open your plans" }).ClickAsync();
        await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("A little breathing room");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        (await page.Locator(".hero-amount").InnerTextAsync()).ShouldBe("$0.00");
    }

    private static async Task VerifyApiGuardsAsync(IPage page)
    {
        var planPath = new Uri(page.Url).AbsolutePath;
        var planResponse = await page.APIRequest.GetAsync($"/api{planPath}");
        planResponse.Status.ShouldBe(200);
        planResponse.Headers["cache-control"].ShouldBe("no-cache, no-store");
        planResponse.Headers["pragma"].ShouldBe("no-cache");
        var plan = (await planResponse.JsonAsync()).ShouldNotBeNull();
        var version = plan.GetProperty("version").GetInt64();
        var withoutToken = await page.APIRequest.PutAsync($"/api{planPath}/settings", new()
        {
            DataObject = new { version, name = "Must not change", notes = "" },
        });
        withoutToken.Status.ShouldBe(400);
        var tokenResponse = await page.APIRequest.GetAsync("/api/plans/token");
        tokenResponse.Headers["cache-control"].ShouldBe("no-cache, no-store");
        tokenResponse.Headers["pragma"].ShouldBe("no-cache");
        var token = (await tokenResponse.JsonAsync()).ShouldNotBeNull().GetProperty("token").GetString().ShouldNotBeNull();
        var stale = await page.APIRequest.PutAsync($"/api{planPath}/settings", new()
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = token },
            DataObject = new { version = version - 1, name = "Must not change", notes = "" },
        });
        stale.Status.ShouldBe(409);
        var unchanged = (await (await page.APIRequest.GetAsync($"/api{planPath}")).JsonAsync()).ShouldNotBeNull();
        unchanged.GetProperty("name").GetString().ShouldBe("A little breathing room");
        unchanged.GetProperty("version").GetInt64().ShouldBe(version);
    }

    private static async Task AddAccountAndAssignAsync(IPage page)
    {
        await Button(page, "Add account").ClickAsync();
        await VerifyEditorFocusAsync(page);
        await VerifyDropdownChromeAsync(page);
        await Input(page, "Account name").FillAsync("Everyday checking");
        await Input(page, "Opening balance").FillAsync("800 + 200");
        await Input(page, "Opening balance").PressAsync("Tab");
        await Button(page, "Save account").ClickAsync();
        await page.Locator(".hero-amount").Filter(new() { HasText = "$1,000.00" }).WaitForAsync();
        await Input(page, "Assign to Groceries").FillAsync("250 + 50");
        await Input(page, "Assign to Groceries").PressAsync("Tab");
        await page.Locator(".hero-amount").Filter(new() { HasText = "$700.00" }).WaitForAsync();
    }

    private static async Task PurchaseAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "All transactions", Exact = true }).ClickAsync();
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        await Button(page, "+ Add transaction").ClickAsync();
        await VerifyEditorFocusAsync(page);
        await VerifyDropdownChromeAsync(page);
        await Input(page, "Payee").FillAsync("Neighborhood market");
        await Input(page, "Amount").FillAsync("50 + 25");
        await Input(page, "Amount").PressAsync("Tab");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Category", Exact = true }).ClickAsync();
        await page.Locator("fluent-option[text='Groceries']").ClickAsync();
        await Button(page, "Save transaction").ClickAsync();
        await page.Locator(".register-table").GetByRole(AriaRole.Button, new() { Name = "Neighborhood market", Exact = true }).WaitForAsync();
    }

    private static async Task ReconcileAsync(IPage page)
    {
        await page.Locator(".account-nav-link").Filter(new() { HasText = "Everyday checking" }).ClickAsync();
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        await Button(page, "Uncleared").ClickAsync();
        await Button(page, "Cleared").WaitForAsync();
        await Button(page, "Reconcile").ClickAsync();
        await VerifyEditorFocusAsync(page);
        (await Input(page, "Bank's posted balance").InputValueAsync()).ShouldBe("925.00");
        await Button(page, "Finish reconciliation").ClickAsync();
        await Button(page, "Reconciled").WaitForAsync();
        var balance = page.Locator(".stat").Filter(new() { HasText = "Cleared balance" });
        (await balance.Locator("strong").InnerTextAsync()).ShouldBe("$925.00");
        await page.GetByRole(AriaRole.Button, new() { Name = "Neighborhood market", Exact = true }).ClickAsync();
        await VerifyEditorFocusAsync(page);
        (await page.Locator(".editor-surface .notice").InnerTextAsync()).ShouldContain("Mark uncleared / unreconcile");
        // Playwright's native enabled check does not recognize Fluent's custom
        // element host; its disabled attribute is the component contract.
        (await Button(page, "Save transaction").GetAttributeAsync("disabled")).ShouldNotBeNull();
    }

    private static async Task VerifyEditorFocusAsync(IPage page)
    {
        await page.WaitForFunctionAsync("document.activeElement?.matches('.editor-surface h2') === true");
        var heading = (await page.Locator(".editor-surface h2").BoundingBoxAsync()).ShouldNotBeNull();
        heading.Y.ShouldBeGreaterThanOrEqualTo(0);
        (heading.Y + heading.Height).ShouldBeLessThanOrEqualTo((await page.EvaluateAsync<int>("innerHeight")));
    }

    private static ILocator Input(IPage page, string label) => page.Locator("fluent-field")
        .Filter(new() { Has = page.GetByText(label, new() { Exact = true }) }).Locator("input");

    private static async Task VerifyDropdownChromeAsync(IPage page)
    {
        var controls = page.Locator("fluent-dropdown > button[slot='control']");
        (await controls.CountAsync()).ShouldBeGreaterThan(0);
        foreach (var control in await controls.AllAsync())
        {
            (await control.EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth")).ShouldBe("0px");
            (await control.EvaluateAsync<string>("element => getComputedStyle(element).padding")).ShouldBe("0px");
            (await control.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= parseFloat(getComputedStyle(element).lineHeight)")).ShouldBeTrue();
        }
    }

    private static async Task VerifyBudgetLayoutAsync(IPage page, int width)
    {
        await VerifyDropdownChromeAsync(page);
        // Native category actions must retain their link-like appearance after
        // scoping the global button styles away from Fluent's slotted controls.
        (await page.Locator(".category-link").First.EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth")).ShouldBe("0px");
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        if (width >= 1000)
        {
            var refresh = (await Button(page, "Refresh").BoundingBoxAsync()).ShouldNotBeNull();
            var status = (await page.Locator(".workspace-status").BoundingBoxAsync()).ShouldNotBeNull();
            Math.Abs(refresh.Y + refresh.Height / 2 - status.Y - status.Height / 2).ShouldBeLessThan(1);
            var filter = (await page.Locator(".table-toolbar fluent-dropdown").BoundingBoxAsync()).ShouldNotBeNull();
            var addCategory = (await Button(page, "+ Category").BoundingBoxAsync()).ShouldNotBeNull();
            Math.Abs(filter.Y + filter.Height - addCategory.Y - addCategory.Height).ShouldBeLessThanOrEqualTo(4);
        }
        await page.GetByRole(AriaRole.Combobox, new() { Name = "View", Exact = true }).ClickAsync();
        await page.Locator("fluent-option[text='Hidden']").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "No categories match this view" }).WaitForAsync();
        await page.GetByRole(AriaRole.Combobox, new() { Name = "View", Exact = true }).ClickAsync();
        await page.Locator("fluent-option[text='All categories']").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Groceries", Exact = true }).WaitForAsync();
    }

    // Fluent v5 declares its button role through ElementInternals, which role locators cannot inspect.
    private static ILocator Button(IPage page, string label) => page.Locator("fluent-button")
        .Filter(new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(label)}\\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)) });
}
