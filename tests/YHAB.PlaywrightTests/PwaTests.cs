using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
[Collection<BrowserAppHostDefinition>]
public sealed class PwaTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task InstallationAndOfflineFallbackPreserveOnlineOnlyDataAsync(int width)
    {
        await RunInBrowserAsync(width, VerifyInstallationAndOfflineAsync, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdateWaitsForConsentAndPreservesOtherTabsAsync()
    {
        await RunInBrowserAsync(1280, VerifyUpdatesAsync, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AlreadyInstallingWorkerPromptsOnlyForAnExistingInstallationAsync(bool hasController)
    {
        await RunInBrowserAsync(1280, (page, _) => VerifyAlreadyInstallingUpdateAsync(page, hasController), TestContext.Current.CancellationToken);
    }

    private async Task RunInBrowserAsync(int width, Func<IPage, IBrowserContext, Task> verify, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
            ViewportSize = new() { Width = width, Height = 900 },
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        try
        {
            // Observe the application's own registration attempt. A second register()
            // call here could hide broken startup code. Keep the original rejection
            // so a missing browser trust prerequisite fails with its actual cause.
            await page.AddInitScriptAsync("""
                (() => {
                    const register = ServiceWorkerContainer.prototype.register;
                    ServiceWorkerContainer.prototype.register = async function (...args) {
                        try { return await register.apply(this, args); }
                        catch (error) {
                            window.yhabWorkerRegistrationError = String(error);
                            throw error;
                        }
                    };
                })();
                """);
            await verify(page, context);
        }
        finally
        {
            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"pwa-{width}-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    private static async Task VerifyInstallationAndOfflineAsync(IPage page, IBrowserContext context)
    {
        await OpenControlledPageAsync(page);
        (await page.Locator("#pwa-update").IsVisibleAsync()).ShouldBeFalse();
        (await page.Locator("link[rel='apple-touch-icon']").GetAttributeAsync("sizes")).ShouldBe("180x180");
        var manifest = await page.EvaluateAsync<JsonElement>("async () => (await fetch(document.querySelector('link[rel=manifest]').href)).json()");
        manifest.GetProperty("name").GetString().ShouldBe("YHAB");
        manifest.GetProperty("display").GetString().ShouldBe("standalone");
        manifest.GetProperty("start_url").GetString().ShouldBe("/plans");
        foreach (var icon in manifest.GetProperty("icons").EnumerateArray())
        {
            (await page.EvaluateAsync<bool>("async src => { const image = new Image(); image.src = src; await image.decode(); return image.naturalWidth >= 192; }", icon.GetProperty("src").GetString())).ShouldBeTrue();
        }

        await VerifyInstallActionAsync(page);
        var origin = await page.EvaluateAsync<double>("performance.timeOrigin");
        await page.Locator(".site-name").ClickAsync();
        await page.Locator(".welcome h1").WaitForAsync();
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);

        // Authenticated requests still use the server and must not populate our cache.
        await BudgetWorkflowTests.RegisterAndLoginAsync(page);
        (await page.EvaluateAsync<int>("async () => (await fetch('/api/plans')).status")).ShouldBe(200);
        await AssertPublicCacheOnlyAsync(page);
        origin = await page.EvaluateAsync<double>("performance.timeOrigin");
        await context.SetOfflineAsync(true);
        foreach (var method in new[] { "GET", "POST" })
        {
            (await page.EvaluateAsync<bool>("async method => { try { await fetch('/api/plans', { method }); return true; } catch { return false; } }", method)).ShouldBeFalse();
        }

        // Exercise Blazor's enhanced GET, not just full document navigation.
        await page.Locator(".site-name").ClickAsync();
        await AssertOfflineScreenAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"pwa-offline-{page.ViewportSize?.Width}.png"), FullPage = true });
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);
        await context.SetOfflineAsync(false);
        await page.GetByRole(AriaRole.Link, new() { Name = "Try again", Exact = true }).ClickAsync();
        await page.Locator(".welcome h1").WaitForAsync();

        await page.GotoAsync("/Account/Manage");
        await page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true }).WaitForAsync();
        var deepUrl = page.Url;
        await context.SetOfflineAsync(true);
        var response = await page.ReloadAsync();
        response.ShouldNotBeNull().Status.ShouldBe(503);
        page.Url.ShouldBe(deepUrl);
        await AssertOfflineScreenAsync(page);
        await AssertPublicCacheOnlyAsync(page);
        await context.SetOfflineAsync(false);
        await page.GetByRole(AriaRole.Link, new() { Name = "Try again", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true }).WaitForAsync();
        page.Url.ShouldBe(deepUrl);
    }

    private static async Task VerifyInstallActionAsync(IPage page)
    {
        // OS installation UI is browser-owned. Exercise our prompt handling with
        // the same event contract, without claiming to automate that native UI.
        await page.EvaluateAsync("""
            () => {
                const prompt = new Event('beforeinstallprompt', { cancelable: true });
                prompt.prompt = async () => { document.documentElement.dataset.installRequested = 'true'; };
                prompt.userChoice = Promise.resolve({ outcome: 'accepted' });
                window.dispatchEvent(prompt);
            }
            """);
        await page.Locator("[data-pwa-install-help] summary").ClickAsync();
        await page.Locator("[data-pwa-install]").ClickAsync();
        (await page.Locator("html").GetAttributeAsync("data-install-requested")).ShouldBe("true");
        await page.EvaluateAsync("window.dispatchEvent(new Event('appinstalled'))");
        (await page.Locator("[data-pwa-install-message]").InnerTextAsync()).ShouldBe("YHAB is installed.");
        (await page.Locator("[data-pwa-install-action]").IsVisibleAsync()).ShouldBeFalse();
    }

    private static async Task AssertOfflineScreenAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Heading, new() { Name = "YHAB can’t connect right now", Exact = true }).WaitForAsync();
        (await page.Locator("main img").EvaluateAsync<bool>("async image => { await image.decode(); return image.naturalWidth === 192; }")).ShouldBeTrue();
        (await page.Locator(".retry").EvaluateAsync<string>("link => getComputedStyle(link).display")).ShouldBe("inline-block");
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        (await page.Locator("main").InnerTextAsync()).ShouldContain("If a save was interrupted");
    }

    private static async Task AssertPublicCacheOnlyAsync(IPage page)
    {
        var paths = await page.EvaluateAsync<string[]>("""
            async () => {
                const paths = [];
                for (const name of await caches.keys()) {
                    if (!name.startsWith('yhab-offline-')) continue;
                    for (const request of await (await caches.open(name)).keys()) paths.push(new URL(request.url).pathname);
                }
                return paths.sort();
            }
            """);
        paths.ShouldBe(["/icons/icon-192.png", "/offline.html", "/pwa/offline.css"]);
    }

    private static async Task VerifyUpdatesAsync(IPage page, IBrowserContext context)
    {
        await OpenControlledPageAsync(page);
        var other = await context.NewPageAsync();
        await OpenControlledPageAsync(other);
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync("first@example.test");
        await other.GetByLabel("Email", new() { Exact = true }).FillAsync("second@example.test");
        var firstOrigin = await page.EvaluateAsync<double>("performance.timeOrigin");
        var secondOrigin = await other.EvaluateAsync<double>("performance.timeOrigin");

        // A distinct script URL starts a real worker update without mutating shared
        // build outputs underneath concurrent browser tests or adding a test API.
        await page.EvaluateAsync("""
            async version => {
                await caches.open('yhab-offline-obsolete');
                await caches.open('unrelated-test-cache');
                await navigator.serviceWorker.register('/service-worker.js?test=' + version, { scope: '/', updateViaCache: 'none' });
            }
            """, Guid.NewGuid().ToString("N"));
        await page.WaitForFunctionAsync("async () => Boolean((await navigator.serviceWorker.getRegistration()).waiting)");
        await page.Locator("#pwa-update").WaitForAsync();
        await other.Locator("#pwa-update").WaitForAsync();
        (await page.GetByLabel("Email", new() { Exact = true }).InputValueAsync()).ShouldBe("first@example.test");
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(firstOrigin);

        await other.Locator("[data-pwa-later]").ClickAsync();
        (await other.Locator("#pwa-update").IsVisibleAsync()).ShouldBeFalse();
        page.Dialog += DismissDialog;
        await page.Locator("[data-pwa-reload]").ClickAsync();
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(firstOrigin);
        (await page.GetByLabel("Email", new() { Exact = true }).InputValueAsync()).ShouldBe("first@example.test");
        page.Dialog -= DismissDialog;
        page.Dialog += AcceptDialog;
        await page.Locator("[data-pwa-reload]").ClickAsync();
        await page.WaitForFunctionAsync("previous => performance.timeOrigin !== previous", firstOrigin);
        await page.GetByLabel("Email", new() { Exact = true }).WaitForAsync();
        await other.Locator("#pwa-update").WaitForAsync();
        (await other.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(secondOrigin);
        (await other.GetByLabel("Email", new() { Exact = true }).InputValueAsync()).ShouldBe("second@example.test");
        var caches = await page.EvaluateAsync<string[]>("caches.keys()");
        caches.ShouldNotContain("yhab-offline-obsolete", StringComparer.Ordinal);
        caches.ShouldContain("unrelated-test-cache", StringComparer.Ordinal);
        await AssertPublicCacheOnlyAsync(page);
    }

    private static async Task VerifyAlreadyInstallingUpdateAsync(IPage page, bool hasController)
    {
        // Control the browser event ordering while loading the real app/module:
        // updatefound has fired before register resolves, but installation is pending.
        await page.AddInitScriptAsync($$"""
            const installing = new EventTarget();
            installing.state = 'installing';
            const registration = new EventTarget();
            registration.installing = installing;
            registration.waiting = null;
            registration.update = async () => registration;
            const serviceWorker = new EventTarget();
            serviceWorker.controller = {{(hasController ? "{}" : "null")}};
            serviceWorker.register = async () => {
                registration.dispatchEvent(new Event('updatefound'));
                window.pwaRegistrationReturned = true;
                return registration;
            };
            Object.defineProperty(navigator, 'serviceWorker', { value: serviceWorker });
            window.finishPwaInstallation = () => {
                registration.installing = null;
                registration.waiting = installing;
                installing.state = 'installed';
                installing.dispatchEvent(new Event('statechange'));
            };
            """);
        await page.GotoAsync("/Account/Login");
        await page.WaitForFunctionAsync("() => window.pwaRegistrationReturned === true");
        (await page.Locator("#pwa-update").IsVisibleAsync()).ShouldBeFalse();
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync("unsaved@example.test");
        var origin = await page.EvaluateAsync<double>("performance.timeOrigin");
        await page.EvaluateAsync("window.finishPwaInstallation()");
        (await page.Locator("#pwa-update").IsVisibleAsync()).ShouldBe(hasController);
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);
        (await page.GetByLabel("Email", new() { Exact = true }).InputValueAsync()).ShouldBe("unsaved@example.test");
    }

    private static async Task OpenControlledPageAsync(IPage page)
    {
        await page.GotoAsync("/Account/Login");
        await page.GetByLabel("Email", new() { Exact = true }).WaitForAsync();
        (await page.EvaluateAsync<string>("async () => (await fetch('/service-worker.js')).headers.get('content-type')")).ShouldStartWith("text/javascript");
        await page.WaitForFunctionAsync("() => Boolean(window.yhabWorkerRegistrationError) || (Boolean(navigator.serviceWorker.controller) && Boolean(customElements.get('fluent-button')))");
        (await page.EvaluateAsync<string?>("() => window.yhabWorkerRegistrationError ?? null"))
            .ShouldBeNull("The application's service-worker registration must succeed; check the browser's HTTPS certificate trust if it fails.");
    }

    private static async void DismissDialog(object? sender, IDialog dialog) => await dialog.DismissAsync();

    private static async void AcceptDialog(object? sender, IDialog dialog) => await dialog.AcceptAsync();
}
