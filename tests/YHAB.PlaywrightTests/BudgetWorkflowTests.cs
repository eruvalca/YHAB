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
            await VerifyWheelScrollingAsync(page);
            await VerifyBudgetLayoutAsync(page, width);
            await VerifyCategoryEditorLayoutAsync(page);
            await VerifyApiGuardsAsync(page);
            await AddAccountAndAssignAsync(page);
            await VerifyStickySummaryAsync(page, width);
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
            await VerifyPayeeRenameUndoAsync(page);
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

    internal static async Task RegisterAndLoginAsync(IPage page, Uri? origin = null)
    {
        string Address(string path) => origin is null ? path : new Uri(origin, path).AbsoluteUri;
        var anonymousPlans = await page.APIRequest.GetAsync(Address("/api/plans"), new() { IgnoreHTTPSErrors = true });
        anonymousPlans.Status.ShouldBe(401);
        await anonymousPlans.DisposeAsync();
        var email = $"budget-{Guid.NewGuid():N}@example.test";
        const string Password = "Local-test-Only!7926";
        await page.GotoAsync(Address("/Account/Register"));
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByLabel("Confirm Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
        await page.Locator(".welcome h1").WaitForAsync();
        await using (var plans = await page.APIRequest.GetAsync(Address("/api/plans"), new() { IgnoreHTTPSErrors = true }))
        {
            plans.Status.ShouldBe(200);
        }
        // A later password login must also work while the email remains unverified.
        await page.Context.ClearCookiesAsync();
        await page.GotoAsync(Address("/Account/Login"));
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
        var workspace = await page.APIRequest.GetAsync($"/api{planPath}/workspace");
        try
        {
            workspace.Status.ShouldBe(200);
            workspace.Headers["cache-control"].ShouldBe("no-cache, no-store");
            var combined = (await workspace.JsonAsync()).ShouldNotBeNull();
            combined.GetProperty("view").GetProperty("catalog").GetProperty("version").GetInt64().ShouldBe(version);
            combined.GetProperty("month").GetProperty("readyToAssign").GetDecimal().ShouldBe(0);
        }
        finally { await workspace.DisposeAsync(); }
        var invalidMonth = await page.APIRequest.GetAsync($"/api{planPath}/workspace?month=1999-12-31");
        try { invalidMonth.Status.ShouldBe(400); }
        finally { await invalidMonth.DisposeAsync(); }
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

    private static async Task VerifyTextUndoOwnershipAsync(IPage page)
    {
        var planPath = new Uri(page.Url).AbsolutePath.Split("/accounts", StringSplitOptions.None)[0];
        var endpoint = $"/api{planPath}";
        var before = (await (await page.APIRequest.GetAsync(endpoint)).JsonAsync()).ShouldNotBeNull();
        var payee = Input(page, "Payee");
        await payee.FocusAsync();
        await payee.PressSequentiallyAsync("Text belongs to this field");
        await page.EvaluateAsync("""
            () => {
                window.historyKeyEvents = [];
                document.addEventListener('keydown', event => {
                    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z')
                        window.historyKeyEvents.push(event.defaultPrevented);
                });
            }
            """);
        await payee.PressAsync("Control+z");
        (await payee.InputValueAsync()).ShouldBeEmpty();
        await payee.PressAsync("Meta+z");
        (await page.EvaluateAsync<bool[]>("window.historyKeyEvents")).ShouldBe([false, false]);
        var after = (await (await page.APIRequest.GetAsync(endpoint)).JsonAsync()).ShouldNotBeNull();
        after.GetProperty("version").GetInt64().ShouldBe(before.GetProperty("version").GetInt64());
        after.GetProperty("transactions").GetArrayLength().ShouldBe(before.GetProperty("transactions").GetArrayLength());
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
        await VerifyAmountEditingAsync(page);
    }

    private static async Task PurchaseAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "All transactions", Exact = true }).ClickAsync();
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        await VerifyRegisterAlignmentAsync(page);
        await Button(page, "+ Add transaction").ClickAsync();
        await VerifyEditorFocusAsync(page);
        await VerifyDropdownChromeAsync(page);
        await VerifyTextUndoOwnershipAsync(page);
        await Input(page, "Payee").FillAsync("Neighborhood market");
        await Input(page, "Amount").FillAsync("50 + 25");
        await Input(page, "Amount").PressAsync("Tab");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Category", Exact = true }).ClickAsync();
        await page.Locator("fluent-option[text='Groceries']").ClickAsync();
        await Button(page, "Save transaction").ClickAsync();
        await page.Locator(".register-table").GetByRole(AriaRole.Button, new() { Name = "Neighborhood market", Exact = true }).WaitForAsync();
        await Button(page, "+ Add transaction").ClickAsync();
        await VerifyEditorFocusAsync(page);
        // Fluent copies the datalist into its shadow root. Check the list bound to
        // the native input, not both the source list and the component's copy.
        (await Input(page, "Payee").EvaluateAsync<string[]>("input => Array.from(input.list?.options ?? [], option => option.value)"))
            .ShouldBe(["Neighborhood market"]);
        await Button(page, "Close").ClickAsync();
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

    private static async Task VerifyPayeeRenameUndoAsync(IPage page)
    {
        await Button(page, "Close").ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Plan settings", Exact = true }).ClickAsync();
        await page.GetByLabel("Existing payee", new() { Exact = true }).SelectOptionAsync("Neighborhood market");
        await page.GetByLabel("New name", new() { Exact = true }).FillAsync("Community market");
        await page.GetByRole(AriaRole.Button, new() { Name = "Rename payee", Exact = true }).ClickAsync();
        await page.Locator(".notice[data-kind='success']").Filter(new() { HasText = "Payee renamed." }).WaitForAsync();
        await page.Locator(".account-nav-link").Filter(new() { HasText = "Everyday checking" }).ClickAsync();
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        await page.Locator(".register-table").GetByRole(AriaRole.Button, new() { Name = "Community market", Exact = true }).WaitForAsync();
        await Button(page, "Undo").ClickAsync();
        await page.Locator(".register-table").GetByRole(AriaRole.Button, new() { Name = "Neighborhood market", Exact = true }).WaitForAsync();
        await Button(page, "Redo").ClickAsync();
        await page.Locator(".register-table").GetByRole(AriaRole.Button, new() { Name = "Community market", Exact = true }).WaitForAsync();
        await Button(page, "Reconciled").WaitForAsync();
        (await page.Locator(".stat").Filter(new() { HasText = "Cleared balance" }).Locator("strong").InnerTextAsync()).ShouldBe("$925.00");
        await Button(page, "+ Add transaction").ClickAsync();
        (await Input(page, "Payee").EvaluateAsync<string[]>("input => Array.from(input.list?.options ?? [], option => option.value)"))
            .ShouldBe(["Community market"]);
        await Button(page, "Close").ClickAsync();
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

    private static async Task VerifyWheelScrollingAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Heading, new() { Name = "Plan your money", Exact = true }).ClickAsync();
        await page.Keyboard.PressAsync("Control+Home");
        await page.WaitForFunctionAsync("scrollY === 0");
        await page.Mouse.MoveAsync((await page.EvaluateAsync<int>("innerWidth")) - 40, 700);
        await page.Mouse.WheelAsync(0, 900);
        await page.WaitForFunctionAsync("scrollY > 100");
        await page.Mouse.WheelAsync(0, 10000);
        await page.WaitForFunctionAsync("scrollY + innerHeight >= document.documentElement.scrollHeight - 2");
    }

    private static async Task VerifyCategoryEditorLayoutAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Groceries", Exact = true }).ClickAsync();
        await VerifyEditorFocusAsync(page);
        var checkboxes = page.Locator(".editor-surface fluent-checkbox");
        (await checkboxes.CountAsync()).ShouldBe(2);
        foreach (var checkbox in await checkboxes.AllAsync())
        {
            var box = (await checkbox.BoundingBoxAsync()).ShouldNotBeNull();
            box.Width.ShouldBeLessThanOrEqualTo(24);
            box.Height.ShouldBeLessThanOrEqualTo(24);
        }
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        await Button(page, "Close").ClickAsync();
    }

    private static async Task VerifyAmountEditingAsync(IPage page)
    {
        var amount = Input(page, "Assign to Groceries");
        var original = (await amount.ElementHandleAsync()).ShouldNotBeNull();
        await amount.ClickAsync();
        (await amount.EvaluateAsync<bool>("input => input.selectionStart === 0 && input.selectionEnd === input.value.length")).ShouldBeTrue();
        await amount.PressAsync("Backspace");
        await amount.PressAsync("Tab");
        await page.Locator(".hero-amount").Filter(new() { HasText = "$1,000.00" }).WaitForAsync();
        (await amount.InputValueAsync()).ShouldBe("0.00");
        (await original.EvaluateAsync<bool>("input => input.isConnected")).ShouldBeTrue();
        await amount.ClickAsync();
        await amount.PressAsync("Backspace");
        await amount.PressAsync("Tab");
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('fluent-field')].find(field => field.querySelector('label')?.textContent === 'Assign to Groceries')?.querySelector('fluent-text-input')?.shadowRoot?.querySelector('input')?.value === '0.00'");
        (await page.GetByRole(AriaRole.Button, new() { Name = "Utilities", Exact = true })
            .EvaluateAsync<bool>("button => document.activeElement === button")).ShouldBeTrue();
        await page.Keyboard.PressAsync("Tab");
        var next = Input(page, "Assign to Utilities");
        (await next.EvaluateAsync<bool>("input => input.getRootNode().activeElement === input && input.selectionStart === 0 && input.selectionEnd === input.value.length")).ShouldBeTrue();
        (await page.Locator(".budget-table [role='alert']").CountAsync()).ShouldBe(0);
        await amount.ClickAsync();
        await page.Keyboard.TypeAsync("300");
        await amount.PressAsync("Tab");
        await page.Locator(".hero-amount").Filter(new() { HasText = "$700.00" }).WaitForAsync();
        (await amount.InputValueAsync()).ShouldBe("300.00");
        await original.DisposeAsync();
    }

    private static async Task VerifyStickySummaryAsync(IPage page, int width)
    {
        var amount = page.Locator(".budget-table tbody fluent-text-input input").Last;
        await amount.FillAsync("700");
        await amount.PressAsync("Tab");
        await page.Locator(".hero-amount").Filter(new() { HasText = "$0.00" }).WaitForAsync();
        var summary = (await page.Locator(".budget-summary").BoundingBoxAsync()).ShouldNotBeNull();
        summary.Y.ShouldBeInRange(0, 16);
        summary.Height.ShouldBeLessThan(width < 768 ? 250 : 155);
        (await amount.BoundingBoxAsync()).ShouldNotBeNull().Y.ShouldBeGreaterThan(summary.Y + summary.Height);
        await amount.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Shift+Tab");
        (await page.EvaluateAsync<double>("() => { let element = document.activeElement; while (element.shadowRoot?.activeElement) element = element.shadowRoot.activeElement; return element.getBoundingClientRect().top - document.querySelector('.budget-summary').getBoundingClientRect().bottom; }")).ShouldBeGreaterThan(0);
        await Button(page, "Undo").ClickAsync();
        await page.Locator(".hero-amount").Filter(new() { HasText = "$700.00" }).WaitForAsync();
    }

    private static async Task VerifyRegisterAlignmentAsync(IPage page)
    {
        if ((await page.EvaluateAsync<int>("innerWidth")) < 1000)
        {
            return;
        }
        var search = (await page.Locator(".table-toolbar fluent-text-input").BoundingBoxAsync()).ShouldNotBeNull();
        foreach (var dropdown in await page.Locator(".table-toolbar fluent-dropdown").AllAsync())
        {
            var box = (await dropdown.BoundingBoxAsync()).ShouldNotBeNull();
            Math.Abs(search.Y - box.Y).ShouldBeLessThan(1);
        }
    }

    private static async Task VerifyDropdownChromeAsync(IPage page)
    {
        foreach (var dropdown in await page.Locator(".editor-surface fluent-dropdown").AllAsync())
        {
            // Fluent's inner border has its own minimum width. Checking only
            // document/host overflow misses a border protruding into card padding.
            (await dropdown.EvaluateAsync<bool>("host => host.shadowRoot.querySelector('.control').getBoundingClientRect().right <= host.getBoundingClientRect().right + 1")).ShouldBeTrue();
        }
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
