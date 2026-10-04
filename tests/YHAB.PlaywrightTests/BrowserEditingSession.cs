using System.Diagnostics;
using System.Globalization;
using Microsoft.Playwright;
using Shouldly;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.PlaywrightTests;

internal static class BrowserEditingSession
{
    public static async Task RunAsync(IPage page, IBrowserContext context, IBrowser browser, string path, string category, int cycles, Action<string> write, CancellationToken token)
    {
        await page.SetViewportSizeAsync(1440, 960);
        var origin = await page.EvaluateAsync<double>("performance.timeOrigin");
        var timer = Stopwatch.StartNew();
        var warmDomNodes = 0;
        // Pace an extended editing session; this is offered load, not a readiness wait.
        using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(1));
        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            token.ThrowIfCancellationRequested();
            await page.EvaluateAsync("globalThis.yhabPreviousRegister = new WeakRef(document.querySelector('.register-table'))");
            await page.GetByRole(AriaRole.Link, new() { Name = "Plan your money", Exact = true }).ClickAsync();
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            var amount = page.GetByRole(AriaRole.Textbox, new() { Name = $"Assign to {category}", Exact = true });
            var saved = await page.RunAndWaitForResponseAsync(async () =>
            {
                await amount.FillAsync(cycle.ToString(CultureInfo.InvariantCulture));
                await amount.PressAsync("Tab");
            }, response => response.Url.EndsWith("/assignments", StringComparison.Ordinal) && string.Equals(response.Request.Method, "PUT", StringComparison.Ordinal));
            saved.Status.ShouldBe(200);
            // The catalog refresh completes before the month projection. Await its
            // displayed total, not the input's temporary optimistic draft value.
            await page.Locator(".budget-total").Filter(new() { Has = page.GetByText("Assigned", new() { Exact = true }) })
                .Locator("strong").Filter(new() { HasText = BudgetFacts.Money(cycle) }).WaitForAsync();
            await page.WaitForFunctionAsync("expected => [...document.querySelectorAll('fluent-field')].find(field => field.querySelector('label')?.textContent === expected.label)?.querySelector('fluent-text-input')?.shadowRoot?.querySelector('input')?.value === expected.value", new { label = $"Assign to {category}", value = $"{cycle}.00" });
            await page.Locator(".workspace-status").Filter(new() { HasText = "All changes saved" }).WaitForAsync();
            (await amount.InputValueAsync()).ShouldBe($"{cycle}.00");
            await page.GetByRole(AriaRole.Link, new() { Name = "All transactions", Exact = true }).ClickAsync();
            await page.Locator(".pagination > span").Filter(new() { HasText = "Page 1 · 100000 transactions" }).WaitForAsync();
            (await page.Locator(".register-table tbody tr").CountAsync()).ShouldBe(50);
            if (cycle is 1 or 6 or 12 or 30 || cycle % 60 == 0 || cycle == cycles)
            {
                var memory = await BrowserMemory.CaptureAsync(browser, context, page, $"100k / navigation-edit cycle {cycle}", write);
                (await page.EvaluateAsync<bool>("globalThis.yhabPreviousRegister.deref() === undefined")).ShouldBeTrue("removed registers must be collectible after navigation");
                if (cycle == 12) { warmDomNodes = memory.DomNodes; }
                if (cycle >= 30 && memory.DomNodes >= warmDomNodes + 500)
                {
                    await BrowserMemory.SnapshotAsync(context, page, $"navigation-growth-{cycle}");
                }
                if (cycle >= 30) { memory.DomNodes.ShouldBeLessThan(warmDomNodes + 500, "detached controls must not accumulate after warm-up"); }
            }
            if (cycle is 1 or 12 || cycle == cycles)
            {
                await BrowserMemory.SnapshotAsync(context, page, $"navigation-{cycle}");
            }
            await cadence.WaitForNextTickAsync(token);
        }
        (await page.EvaluateAsync<double>("performance.timeOrigin")).ShouldBe(origin);
        write($"100k / {cycles} enhanced-navigation and assignment cycles: {timer.Elapsed.TotalSeconds:F1}s without document reload; bounded 50-row register retained.");
        page.Url.ShouldEndWith($"{path}/accounts");
        if (cycles > 120)
        {
            // Observe idle memory separately from the offered navigation load.
            using var idle = new PeriodicTimer(TimeSpan.FromSeconds(30));
            for (var sample = 1; sample <= 2; sample++)
            {
                await idle.WaitForNextTickAsync(token);
                await BrowserMemory.CaptureAsync(browser, context, page, $"100k / idle {sample * 30}s", write);
            }
        }
    }
}
