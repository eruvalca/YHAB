using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
[Collection<BrowserAppHostDefinition>]
public sealed class PlanCatalogTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    [Trait("Category", "BrowserSmoke")]
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
            var row = page.Locator($"[data-category-id='{category.Id}']");
            if (width >= 1000)
            {
                foreach (var group in plan.Groups.Where(item => item.Id != source.Id && item.Id != destination.Id))
                {
                    await page.GetByRole(AriaRole.Button, new() { Name = $"Collapse {group.Name}", Exact = true }).ClickAsync();
                }
                await PointerDragAsync(page, row.Locator(".row-move"), page.Locator($"[data-category-id='{anchor.Id}']"));
                await page.Locator($"[data-group-id='{destination.Id}'] [data-category-id='{category.Id}']").WaitForAsync();
                await page.Locator($"[data-group-id='{source.Id}'] .group-row .row-move").DragToAsync(page.Locator($"[data-group-id='{destination.Id}'] .group-row"));
                await page.WaitForFunctionAsync("id => document.querySelector('.budget-table [data-group-id]')?.dataset.groupId === id", source.Id.ToString());
            }
            else
            {
                await row.Locator(".row-move").ClickAsync();
                await page.GetByRole(AriaRole.Combobox, new() { Name = "Move to group", Exact = true }).ClickAsync();
                await page.Locator($"fluent-option[text='{destination.Name}']").ClickAsync();
                await page.Locator($"[data-group-id='{destination.Id}'] [data-category-id='{category.Id}']").WaitForAsync();
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

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [Trait("Category", "BrowserSmoke")]
    public async Task CompactPlanNavigationRemainsReachableAfterScrollingAsync(int width)
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
            ViewportSize = new() { Width = width, Height = 640 },
            HasTouch = true,
            IsMobile = true,
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GotoAsync("/plans");
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync($"Reachable planning {width}");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var path = new Uri(page.Url).AbsolutePath;
            var tabs = page.GetByRole(AriaRole.Navigation, new() { Name = "Plan pages", Exact = true });
            (await tabs.IsVisibleAsync()).ShouldBeTrue();
            (await tabs.GetByRole(AriaRole.Link).CountAsync()).ShouldBe(4);
            (await tabs.GetByRole(AriaRole.Link, new() { Name = "Plan", Exact = true }).GetAttributeAsync("aria-current")).ShouldBe("page");
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();

            await page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
            await page.WaitForFunctionAsync("window.scrollY > 0");
            var sticky = (await tabs.BoundingBoxAsync()).ShouldNotBeNull();
            sticky.Y.ShouldBeInRange(-1, 1);
            (sticky.Y + sticky.Height).ShouldBeGreaterThan(0);

            await tabs.GetByRole(AriaRole.Link, new() { Name = "Reflect", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Reflect", Exact = true }).WaitForAsync();
            new Uri(page.Url).AbsolutePath.ShouldBe($"{path}/reports");
            (await tabs.GetByRole(AriaRole.Link, new() { Name = "Reflect", Exact = true }).GetAttributeAsync("aria-current")).ShouldBe("page");
            (await tabs.GetByRole(AriaRole.Link, new() { Name = "Plan", Exact = true }).GetAttributeAsync("aria-current")).ShouldBeNull();
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
            errors.ShouldBeEmpty();
        }
        finally
        {
            await BrowserArtifacts.CaptureAsync(page, context, Path.Combine(AppContext.BaseDirectory, "TestResults", $"plan-page-tabs-{width}"), output.WriteLine);
        }
    }

    [Fact]
    [Trait("Category", "BrowserSmoke")]
    public async Task FirstRunPanelStaysAboveTheTableWhenColumnsWrapAsync()
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
            ViewportSize = new() { Width = 1024, Height = 960 },
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GotoAsync("/plans");
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("Wrapped first run");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var card = (await page.Locator(".first-run-account").BoundingBoxAsync()).ShouldNotBeNull();
            var table = (await page.Locator(".table-toolbar").BoundingBoxAsync()).ShouldNotBeNull();
            card.Y.ShouldBeLessThan(table.Y + 1);
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
            errors.ShouldBeEmpty();
        }
        finally
        {
            await BrowserArtifacts.CaptureAsync(page, context, Path.Combine(AppContext.BaseDirectory, "TestResults", "first-run-1024"), output.WriteLine);
        }
    }

    [Fact]
    public async Task SortableCategoriesMoveByKeyboardAndTouchAsync()
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
            ViewportSize = new() { Width = 390, Height = 960 },
            HasTouch = true,
            IsMobile = true,
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GotoAsync("/plans");
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("Sortable planning");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var path = new Uri(page.Url).AbsolutePath;
            // The cached WebAssembly renderer sends commands over HTTP, so the reorder request can be held.
            await page.WaitForFunctionAsync("() => Object.keys(localStorage).some(key => key.startsWith('blazor-resource-hash:'))", null, new() { Timeout = 60000 });
            await page.ReloadAsync();
            await page.Locator(".workspace[data-renderer='WebAssembly'][data-interactive='true']").WaitForAsync();
            var plan = await ReadAsync(page, path);
            var groups = plan.Groups.Where(item => !item.Hidden).OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray();
            int Count(Guid group) => plan.Categories.Count(item => item.GroupId == group && !item.Hidden);
            var pair = groups.Zip(groups.Skip(1)).First(item => Count(item.First.Id) >= 3 && Count(item.Second.Id) >= 2);
            var (source, destination) = (pair.First.Id, pair.Second.Id);

            // Fluent applies a keyboard cross-list move to the destination first; the source must not then remove a stale index.
            for (var move = 0; move < 2; move++)
            {
                var last = (await DomOrderAsync(page, source))[^1];
                var item = page.Locator($"#category-list-{source:N} > .sortable-item").Filter(new() { Has = page.Locator($"[data-category-id='{last}']") });
                await item.FocusAsync();
                await item.PressAsync("Enter");
                await SavedAsync(page, () => item.PressAsync("ArrowRight"));
                plan = await ReadAsync(page, path);
                plan.Categories.Single(category => string.Equals(category.Id.ToString(), last, StringComparison.Ordinal)).GroupId.ShouldBe(destination);
                (await DomOrderAsync(page, destination)).ShouldBe(PlanOrder(plan, destination));
                (await DomOrderAsync(page, source)).ShouldBe(PlanOrder(plan, source));
            }

            var cdp = await context.NewCDPSessionAsync(page);
            var before = await DomOrderAsync(page, destination);
            await SavedAsync(page, () => TouchDragAsync(page, cdp, Row(page, before[0]).Locator(".row-move"), Row(page, before[1]), async () =>
            {
                (await page.Locator(".category-list .sortable-drag").CountAsync()).ShouldBeGreaterThan(0);
                (await page.Locator(".category-list .sortable-ghost").CountAsync()).ShouldBe(1);
            }));
            plan = await ReadAsync(page, path);
            var within = await DomOrderAsync(page, destination);
            within.ShouldBe(PlanOrder(plan, destination));
            within.ShouldNotBe(before);

            // The destination is the next group, so its first row is adjacent to
            // the source group's last row; stacked phone rows make the group's
            // first row fall outside a viewport-sized synthetic drag.
            var moving = (await DomOrderAsync(page, source))[^1];
            await SavedAsync(page, () => TouchDragAsync(page, cdp, Row(page, moving).Locator(".row-move"), Row(page, within[0]), () => Task.CompletedTask));
            plan = await ReadAsync(page, path);
            plan.Categories.Single(category => string.Equals(category.Id.ToString(), moving, StringComparison.Ordinal)).GroupId.ShouldBe(destination);
            (await DomOrderAsync(page, destination)).ShouldBe(PlanOrder(plan, destination));
            (await DomOrderAsync(page, source)).ShouldBe(PlanOrder(plan, source));

            var held = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
            await page.RouteAsync("**/categories/reorder", route => { held.TrySetResult(route); return Task.CompletedTask; });
            var confirmed = PlanOrder(plan, destination);
            await TouchDragAsync(page, cdp, Row(page, confirmed[0]).Locator(".row-move"), Row(page, confirmed[1]), () => Task.CompletedTask);
            var request = await held.Task.WaitAsync(TimeSpan.FromSeconds(30), timeout.Token);
            (await DomOrderAsync(page, destination)).ShouldNotBe(confirmed);
            await request.FulfillAsync(new()
            {
                Status = 400,
                ContentType = "application/problem+json",
                Body = """{"title":"Rejected","detail":"Reorder rejected for this test."}""",
            });
            await page.GetByText("Reorder rejected for this test.").WaitForAsync();
            await page.UnrouteAsync("**/categories/reorder");
            await page.WaitForFunctionAsync("([id, order]) => [...document.querySelectorAll(`#category-list-${id} [data-category-id]`)].map(row => row.dataset.categoryId).join() === order",
                new object[] { destination.ToString("N"), string.Join(',', confirmed) });
            (await ReadAsync(page, path)).Version.ShouldBe(plan.Version);
            (await page.Locator("#blazor-error-ui").IsVisibleAsync()).ShouldBeFalse();
            errors.ShouldBeEmpty();
        }
        finally
        {
            await BrowserArtifacts.CaptureAsync(page, context, Path.Combine(AppContext.BaseDirectory, "TestResults", "plan-sortable-touch"), output.WriteLine);
        }
    }

    [Fact]
    public async Task SortableCategoriesKeepKeyboardFocusWhenServerRenderIsDelayedAsync()
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
        var allowWebAssembly = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Holding the WebAssembly runtime keeps InteractiveAuto on the server circuit.
        await context.RouteAsync("**/_framework/dotnet.native.*.wasm", async route =>
        {
            await allowWebAssembly.Task.WaitAsync(timeout.Token);
            await route.ContinueAsync();
        });
        // Delay the circuit's server-to-client messages so a render cannot beat Fluent's 50 ms focus timer.
        var slow = new StrongBox<bool>();
        await page.RouteWebSocketAsync("**/_blazor**", socket =>
        {
            var server = socket.ConnectToServer();
            var outgoing = Task.CompletedTask;
            socket.OnMessage(frame => SendWebSocketMessage(server, frame));
            server.OnMessage(frame =>
            {
                var message = frame;
                outgoing = outgoing.ContinueWith(async _ =>
                {
                    if (Volatile.Read(ref slow.Value)) { await Task.Delay(TimeSpan.FromMilliseconds(300), timeout.Token); }
                    SendWebSocketMessage(socket, message);
                }, TaskScheduler.Default).Unwrap();
            });
        });
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GotoAsync("/plans");
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("Delayed planning");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            (await page.Locator(".workspace").GetAttributeAsync("data-renderer")).ShouldBe("Server");
            var path = new Uri(page.Url).AbsolutePath;
            // Create an empty group after the starter groups; the previous group is then one ArrowRight away.
            await Button(page, "Category group").ClickAsync();
            await Input(page, "New group name").FillAsync("Later");
            await Button(page, "Save").ClickAsync();
            await page.WaitForFunctionAsync("() => [...document.querySelectorAll('.group-row')].some(row => row.textContent.includes('Later'))");
            var plan = await ReadAsync(page, path);
            var groups = plan.Groups.Where(item => !item.Hidden).OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray();
            var later = groups.Single(item => string.Equals(item.Name, "Later", StringComparison.Ordinal));
            var source = groups[Array.IndexOf(groups, later) - 1];
            var moved = plan.Categories.Where(item => item.GroupId == source.Id && !item.Hidden).OrderBy(item => item.SortOrder).ThenBy(item => item.Id).Last();
            var item = page.Locator($"#category-list-{source.Id:N} > .sortable-item").Filter(new() { Has = page.Locator($"[data-category-id='{moved.Id}']") });
            await item.ScrollIntoViewIfNeededAsync();
            await item.FocusAsync();
            await item.PressAsync("Enter");
            Volatile.Write(ref slow.Value, true);
            await item.PressAsync("ArrowRight");
            await WaitForPlanAsync(page, path, value => value.Categories.Single(category => category.Id == moved.Id).GroupId == later.Id, timeout.Token);
            await page.WaitForFunctionAsync("id => document.activeElement?.closest('.sortable-item')?.querySelector('[data-category-id]')?.getAttribute('data-category-id') === id",
                moved.Id.ToString(), new() { Timeout = 15000 });
            (await page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('aria-grabbed')")).ShouldBe("true");
            Volatile.Write(ref slow.Value, false);
            // The same focused row must still accept a keyboard move after a delayed server render.
            await page.Keyboard.PressAsync("ArrowLeft");
            plan = await WaitForPlanAsync(page, path, value => value.Categories.Single(category => category.Id == moved.Id).GroupId == source.Id, timeout.Token);
            plan.Categories.Single(category => category.Id == moved.Id).GroupId.ShouldBe(source.Id);
            (await page.Locator("#blazor-error-ui").IsVisibleAsync()).ShouldBeFalse();
            errors.ShouldBeEmpty();
        }
        finally
        {
            allowWebAssembly.TrySetResult();
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors });
            await BrowserArtifacts.CaptureAsync(page, context, Path.Combine(AppContext.BaseDirectory, "TestResults", "plan-sortable-server-focus"), output.WriteLine);
        }
    }

    [Fact]
    public async Task SortableKeyboardMoveKeepsFocusOnTheCategoryAfterRejectionAsync()
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
            await page.GotoAsync("/plans");
            await page.GetByLabel("Plan name", new() { Exact = true }).FillAsync("Keyboard focus planning");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create your plan" }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var path = new Uri(page.Url).AbsolutePath;
            // The cached WebAssembly renderer issues reorder requests the browser can hold or reject.
            await page.WaitForFunctionAsync("() => Object.keys(localStorage).some(key => key.startsWith('blazor-resource-hash:'))", null, new() { Timeout = 60000 });
            await page.ReloadAsync();
            await page.Locator(".workspace[data-renderer='WebAssembly'][data-interactive='true']").WaitForAsync();
            var plan = await ReadAsync(page, path);
            var groups = plan.Groups.Where(item => !item.Hidden).OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray();
            int Count(Guid group) => plan.Categories.Count(item => item.GroupId == group && !item.Hidden);
            var pair = groups.Zip(groups.Skip(1)).First(item => Count(item.First.Id) >= 2 && Count(item.Second.Id) >= 1);
            var (source, destination) = (pair.First.Id, pair.Second.Id);

            // A rejected save resolves the add, so focus must follow the category back to its source row.
            var rejected = plan.Categories.Where(item => item.GroupId == source && !item.Hidden).OrderBy(item => item.SortOrder).ThenBy(item => item.Id).Last();
            var rejectedItem = page.Locator($"#category-list-{source:N} > .sortable-item").Filter(new() { Has = page.Locator($"[data-category-id='{rejected.Id}']") });
            await rejectedItem.ScrollIntoViewIfNeededAsync();
            await rejectedItem.FocusAsync();
            await rejectedItem.PressAsync("Enter");
            var rejection = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
            await page.RouteAsync("**/categories/reorder", route => { rejection.TrySetResult(route); return Task.CompletedTask; });
            await rejectedItem.PressAsync("ArrowRight");
            var request = await rejection.Task.WaitAsync(TimeSpan.FromSeconds(30), timeout.Token);
            await page.Locator($"[data-group-id='{destination}'] [data-category-id='{rejected.Id}']").WaitForAsync(new() { Timeout = 30000 });
            await request.FulfillAsync(new()
            {
                Status = 400,
                ContentType = "application/problem+json",
                Body = """{"title":"Rejected","detail":"Keyboard reorder rejected for this test."}""",
            });
            await page.UnrouteAsync("**/categories/reorder");
            await page.Locator($"[data-group-id='{source}'] [data-category-id='{rejected.Id}']").WaitForAsync(new() { Timeout = 30000 });
            await page.WaitForFunctionAsync("id => document.activeElement?.closest('.sortable-item')?.querySelector('[data-category-id]')?.getAttribute('data-category-id') === id",
                rejected.Id.ToString(), new() { Timeout = 15000 });
            (await page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('aria-grabbed')")).ShouldBe("true");
            (await page.Locator(".sortable-item[aria-grabbed='true']").CountAsync()).ShouldBe(1);
            // The next arrow key moves that same category, not whatever row now occupies the destination index.
            await page.Keyboard.PressAsync("ArrowRight");
            plan = await WaitForPlanAsync(page, path, value => value.Categories.Single(category => category.Id == rejected.Id).GroupId == destination, timeout.Token);
            plan.Categories.Single(category => category.Id == rejected.Id).GroupId.ShouldBe(destination);
            (await page.Locator("#blazor-error-ui").IsVisibleAsync()).ShouldBeFalse();
            errors.ShouldBeEmpty();
        }
        finally
        {
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors });
            await BrowserArtifacts.CaptureAsync(page, context, Path.Combine(AppContext.BaseDirectory, "TestResults", "plan-sortable-keyboard-focus"), output.WriteLine);
        }
    }

    // The server renderer commits reorders without a browser request, so poll the plan instead of a response.
    private static async Task<PlanSnapshot> WaitForPlanAsync(IPage page, string path, Func<PlanSnapshot, bool> condition, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = await ReadAsync(page, path);
            if (condition(plan)) { return plan; }
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private static void SendWebSocketMessage(IWebSocketRoute target, IWebSocketFrame frame)
    {
        if (frame.Binary is { Length: > 0 } binary) { target.Send(binary); }
        else if (frame.Text is { } text) { target.Send(text); }
    }

    private static ILocator Row(IPage page, string id) => page.Locator($".category-list [data-category-id='{id}']");

    private static Task<string[]> DomOrderAsync(IPage page, Guid group)
        => page.Locator($"#category-list-{group:N} [data-category-id]").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.categoryId)");

    private static string[] PlanOrder(PlanSnapshot plan, Guid group) => plan.Categories.Where(item => item.GroupId == group && !item.Hidden)
        .OrderBy(item => item.SortOrder).ThenBy(item => item.Id).Select(item => item.Id.ToString()).ToArray();

    // Waits for the reorder command to commit and the workspace to leave its busy state before reading the result.
    private static async Task SavedAsync(IPage page, Func<Task> action)
    {
        var response = await page.RunAndWaitForResponseAsync(action,
            item => item.Url.EndsWith("/categories/reorder", StringComparison.Ordinal) && string.Equals(item.Request.Method, "POST", StringComparison.Ordinal));
        response.Status.ShouldBe(200, response.Ok ? null : await response.TextAsync());
        await page.WaitForFunctionAsync("() => document.querySelector('.category-list .row-move') && !document.querySelector('.category-list .row-move:not(.sortable-grab)')");
    }

    // Playwright's touchscreen only taps, so the drag is dispatched through the Chromium protocol as real touch input.
    private static async Task TouchDragAsync(IPage page, ICDPSession cdp, ILocator handle, ILocator target, Func<Task> whileDragging)
    {
        await handle.ScrollIntoViewIfNeededAsync();
        // Keep the drag away from the viewport edge so SortableJS auto-scroll does not move the rows mid-gesture.
        await handle.EvaluateAsync("element => element.scrollIntoView({ block: 'center' })");
        var from = (await handle.BoundingBoxAsync()).ShouldNotBeNull();
        var to = (await target.BoundingBoxAsync()).ShouldNotBeNull();
        var (x, y) = (from.X + from.Width / 2, from.Y + from.Height / 2);
        var (endX, endY) = (to.X + to.Width / 2, to.Y + to.Height * 0.75f);
        await TouchAsync(cdp, "touchStart", x, y);
        const int Steps = 16;
        for (var step = 1; step <= Steps; step++)
        {
            await TouchAsync(cdp, "touchMove", x + (endX - x) * step / Steps, y + (endY - y) * step / Steps);
            if (step == Steps / 2)
            {
                await whileDragging();
            }
        }
        await GhostBesideAsync(page, target, after: true);
        await TouchAsync(cdp, "touchEnd", endX, endY);
        await page.Locator(".category-list .sortable-drag").WaitForAsync(new() { State = WaitForSelectorState.Detached });
    }

    // Fallback drags apply the pointer position on a SortableJS timer, so release only once the placeholder row has reached the target.
    private static async Task GhostBesideAsync(IPage page, ILocator target, bool after)
    {
        var id = (await target.GetAttributeAsync("data-category-id")).ShouldNotBeNull();
        await page.WaitForFunctionAsync("([id, after]) => { const ghost = document.querySelector('.category-list .sortable-ghost'); return !!(after ? ghost?.previousElementSibling : ghost?.nextElementSibling)?.querySelector(`[data-category-id='${id}']`); }",
            new object[] { id, after }, new() { Timeout = 10000 });
    }

    private static async Task TouchAsync(ICDPSession cdp, string type, float x, float y) => await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["type"] = type,
        ["touchPoints"] = string.Equals(type, "touchEnd", StringComparison.Ordinal) ? Array.Empty<object>() : new object[] { new Dictionary<string, object>(StringComparer.Ordinal) { ["x"] = x, ["y"] = y } },
    });

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

    // SortableJS uses pointer events, so the drag needs intermediate moves rather than an HTML drag-and-drop event.
    // The drop lands in the target's upper quarter and waits for the placeholder so the row is inserted before it.
    private static async Task PointerDragAsync(IPage page, ILocator handle, ILocator target)
    {
        var from = (await handle.BoundingBoxAsync()).ShouldNotBeNull();
        await page.Mouse.MoveAsync(from.X + from.Width / 2, from.Y + from.Height / 2);
        await page.Mouse.DownAsync();
        var to = (await target.BoundingBoxAsync()).ShouldNotBeNull();
        await page.Mouse.MoveAsync(to.X + to.Width / 2, to.Y + to.Height / 4, new() { Steps = 12 });
        await GhostBesideAsync(page, target, after: false);
        await page.Mouse.UpAsync();
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
