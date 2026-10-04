using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Npgsql;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.Testing;
using YHAB.Tests.Scenarios;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HouseholdBrowserTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GrowingHouseholdLedgerMatchesBudgetRegisterAndReportsAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cycles = int.Parse(Environment.GetEnvironmentVariable("YHAB_BROWSER_CYCLES") ?? "120", CultureInfo.InvariantCulture);
        cycles.ShouldBeInRange(120, 1800);
        timeout.CancelAfter(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(cycles - 120));
        await using var builder = await TestAppHost.CreateAsync(timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        using (var startup = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
        {
            // Reserve the longer overall deadline for actual transaction/UI work.
            startup.CancelAfter(TimeSpan.FromMinutes(2));
            await app.StartAsync(startup.Token);
            await app.ResourceNotifications.WaitForResourceHealthyAsync("yhab", startup.Token);
        }
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
        var artifacts = System.IO.Path.Combine(AppContext.BaseDirectory, "TestResults", $"household-{Guid.NewGuid():N}");
        var tracing = true;
        var allowWebAssembly = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Hold the runtime download so a cold server circuit must work on its own.
        await context.RouteAsync("**/_framework/dotnet.native.*.wasm", async route =>
        {
            runtimeRequested.TrySetResult();
            await allowWebAssembly.Task.WaitAsync(timeout.Token);
            await route.ContinueAsync();
        });
        try
        {
            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            var api = await HouseholdApi.CreateAsync(page.APIRequest);
            await api.CreateCatalogAsync();
            for (var months = 1; months <= 3; months++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                await api.AddMonthAsync(months, timeout.Token);
                output.WriteLine($"{months * 200} transactions: posted 200 more via authenticated API in {timer.ElapsedMilliseconds} ms.");
                timer.Restart();
                var snapshot = await api.ReadAsync();
                await VerifyBudgetAsync(page, api.Path, snapshot.Today, months);
                if (months == 1)
                {
                    await runtimeRequested.Task.WaitAsync(TimeSpan.FromSeconds(30), timeout.Token);
                    (await page.Locator(".workspace").GetAttributeAsync("data-renderer")).ShouldBe("Server");
                    errors.ShouldBeEmpty();
                    allowWebAssembly.SetResult();
                    await context.UnrouteAsync("**/_framework/dotnet.native.*.wasm");
                    // .NET 10 writes this cache marker after the Auto runtime finishes
                    // loading. Leaving the document sooner can cancel its downloads.
                    await page.WaitForFunctionAsync("() => Object.keys(localStorage).some(key => key.startsWith('blazor-resource-hash:'))", null,
                        new() { Timeout = 60000 });
                    output.WriteLine("Cold server circuit verified; WebAssembly resources finished loading.");
                }
                await VerifyRegisterAsync(page, api.Path, months);
                var session = await context.NewCDPSessionAsync(page);
                await session.SendAsync("HeapProfiler.collectGarbage");
                var heap = (await session.SendAsync("Runtime.getHeapUsage")).ShouldNotBeNull();
                var dom = (await session.SendAsync("Memory.getDOMCounters")).ShouldNotBeNull();
                output.WriteLine($"{months * 200} transactions: post-GC browser JS heap {heap.GetProperty("usedSize").GetDouble():N0} bytes; {dom.GetProperty("nodes").GetInt32():N0} DOM nodes. WebAssembly linear memory is not included.");
                await session.DetachAsync();
                await VerifyReportsAsync(page, api.Path, months);
                output.WriteLine($"{months * 200} transactions: browser budget/register/report checks {timer.ElapsedMilliseconds} ms.");
            }

            await page.SetViewportSizeAsync(390, 844);
            await page.GotoAsync($"{api.Path}/accounts");
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            (await page.Locator(".workspace").GetAttributeAsync("data-renderer")).ShouldBe("WebAssembly");
            await page.Locator(".pagination > span").Filter(new() { HasText = "Page 1 · 600 transactions" }).WaitForAsync();
            (await page.Locator(".pagination > span").InnerTextAsync()).ShouldBe("Page 1 · 600 transactions");
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
            // Finish snapshot tracing and replace its document before measuring
            // memory so diagnostic DOM retention cannot look like an app leak.
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
            tracing = false;
            await page.CloseAsync();
            page = await context.NewPageAsync();
            page.PageError += (_, error) => errors.Enqueue(error);
            await page.SetViewportSizeAsync(390, 844);
            var connection = (await app.GetConnectionStringAsync("yhabdb", timeout.Token)).ShouldNotBeNull();
            await VerifyLargeRegistersAsync(page, context, browser, api.Path, connection, timeout.Token);
            await BrowserEditingSession.RunAsync(page, context, browser, api.Path, "Groceries", cycles, output.WriteLine, timeout.Token);
            await VerifyWideCatalogAsync(page, context, browser, connection, timeout.Token);
            errors.ShouldBeEmpty();
        }
        finally
        {
            allowWebAssembly.TrySetResult();
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors });
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine, captureTrace: tracing);
        }
    }

    private async Task VerifyLargeRegistersAsync(IPage page, IBrowserContext context, IBrowser browser, string path, string connectionString, CancellationToken token)
    {
        var id = Guid.Parse(path.Split('/')[2]);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        var previous = 600;
        foreach (var count in new[] { 10_000, 50_000, 100_000 })
        {
            // Only the disposable AppHost database is bulk-seeded. Browser reads use
            // normal authenticated endpoints. Retire history tied to the earlier fixture.
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "BudgetTransaction" ("PlanId", "Id", "AccountId", "Date", "Payee", "Memo", "Amount", "State", "TransferState", "NeedsApproval", "Flag", "Repeat", "Occurrence", "Sequence")
                SELECT @id, gen_random_uuid(), @account, DATE '2026-01-01' + (n % 90)::int,
                    CASE WHEN n % 10 = 0 THEN 'Salary' ELSE 'Market' END, '', CASE WHEN n % 10 = 0 THEN 100 ELSE -1 END,
                    0, 0, false, '', 0, 0, n FROM generate_series(@previous + 1, @count) n;
                INSERT INTO "BudgetSplit" ("PlanId", "Id", "TransactionId", "CategoryId", "Amount", "Memo")
                SELECT "PlanId", gen_random_uuid(), "Id", CASE WHEN "Amount" < 0 THEN @category ELSE NULL END, "Amount", ''
                    FROM "BudgetTransaction" WHERE "PlanId" = @id AND "Sequence" > @previous;
                DELETE FROM "BudgetCheckpoint" WHERE "PlanId" = @id;
                DELETE FROM "BudgetHistory" WHERE "PlanId" = @id;
                DELETE FROM "BudgetReceipt" WHERE "PlanId" = @id;
                UPDATE "BudgetPlans" SET "Version" = "Version" + 1, "NextSequence" = @count, "HistoryCursor" = 0 WHERE "Id" = @id;
                """;
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("account", HouseholdScenario.Id(1));
            command.Parameters.AddWithValue("category", HouseholdScenario.Id(102));
            command.Parameters.AddWithValue("previous", previous);
            command.Parameters.AddWithValue("count", count);
            await command.ExecuteNonQueryAsync(token);
            var timer = Stopwatch.StartNew();
            await page.GotoAsync($"{path}/accounts");
            await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
            await page.Locator(".pagination > span").Filter(new() { HasText = $"Page 1 · {count} transactions" }).WaitForAsync();
            (await page.Locator(".register-table tbody tr").CountAsync()).ShouldBe(50);
            var session = await context.NewCDPSessionAsync(page);
            var readyMilliseconds = timer.ElapsedMilliseconds;
            await session.SendAsync("HeapProfiler.collectGarbage");
            var heap = (await session.SendAsync("Runtime.getHeapUsage")).ShouldNotBeNull();
            var dom = (await session.SendAsync("Memory.getDOMCounters")).ShouldNotBeNull();
            output.WriteLine($"{count:N0} transactions: interactive register {readyMilliseconds} ms; post-GC browser JS heap {heap.GetProperty("usedSize").GetDouble():N0} bytes, {dom.GetProperty("nodes").GetInt32():N0} DOM nodes; exactly 50 rows. JS heap excludes WebAssembly linear memory.");
            await session.DetachAsync();
            (await page.Locator(".workspace").GetAttributeAsync("data-renderer")).ShouldBe("WebAssembly");
            await BrowserMemory.CaptureAsync(browser, context, page, $"{count:N0} entries", output.WriteLine);
            previous = count;
        }
    }

    private async Task VerifyWideCatalogAsync(IPage page, IBrowserContext context, IBrowser browser, string connectionString, CancellationToken token)
    {
        var api = await HouseholdApi.CreateAsync(page.APIRequest);
        var plan = await api.ReadAsync();
        var start = BudgetFacts.Month(plan.Today).AddMonths(-239);
        var group = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "BudgetAccount" ("PlanId", "Id", "Name", "Kind", "OpeningBalance", "OpenedOn", "Closed", "Notes", "InterestRate", "MinimumPayment")
            SELECT @id, gen_random_uuid(), 'Account ' || n, 0, 1000000, @start, false, '', 0, 0 FROM generate_series(1, 100) n;
            INSERT INTO "BudgetGroup" ("PlanId", "Id", "Name", "SortOrder", "Hidden") VALUES (@id, @group, 'Scale categories', 0, false);
            INSERT INTO "BudgetCategory" ("PlanId", "Id", "GroupId", "Name", "Notes", "SortOrder", "Hidden", "TargetCadence", "TargetAmount", "TargetStartMonth", "TargetRepeatMonths", "TargetWeekday")
            SELECT @id, gen_random_uuid(), @group, 'Category ' || n, '', n, false, 0, 0, @start, 0, 0 FROM generate_series(1, 500) n;
            INSERT INTO "BudgetAllocation" ("PlanId", "CategoryId", "Month", "Amount", "Snoozed")
            SELECT @id, "Id", (@start + make_interval(months => n))::date, 1, false
            FROM "BudgetCategory" CROSS JOIN generate_series(0, 239) n WHERE "PlanId" = @id;
            UPDATE "BudgetPlans" SET "Version" = "Version" + 1 WHERE "Id" = @id;
            """;
        command.Parameters.AddWithValue("id", plan.Id);
        command.Parameters.AddWithValue("group", group);
        command.Parameters.AddWithValue("start", start);
        await command.ExecuteNonQueryAsync(token);
        var timer = Stopwatch.StartNew();
        await page.GotoAsync(api.Path);
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        await page.Locator(".hero-amount").Filter(new() { HasText = "$99,880,000.00" }).WaitForAsync();
        (await page.Locator(".budget-table .amount-input input").CountAsync()).ShouldBe(500);
        (await page.Locator(".workspace").GetAttributeAsync("data-renderer")).ShouldBe("WebAssembly");
        output.WriteLine($"100 accounts / 500 categories / 120,000 allocations: interactive budget {timer.Elapsed.TotalMilliseconds:F1}ms; 500 amount inputs, exact $99,880,000 ready to assign.");
        await BrowserMemory.CaptureAsync(browser, context, page, "wide catalog", output.WriteLine);
    }

    private static async Task VerifyBudgetAsync(IPage page, string path, DateOnly today, int months)
    {
        var response = (await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded })).ShouldNotBeNull();
        (await response.TextAsync()).ShouldContain("data-renderer=\"Static\"");
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        var selected = BudgetFacts.Month(today);
        var target = HouseholdScenario.Start.AddMonths(months - 1);
        while (selected > target)
        {
            await page.Locator("fluent-button[aria-label='Previous month']").ClickAsync();
            selected = selected.AddMonths(-1);
            await page.Locator(".month-label").Filter(new() { HasText = selected.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-US")) }).WaitForAsync();
        }

        (await page.Locator(".hero-amount").InnerTextAsync()).ShouldBe(BudgetFacts.Money(13400 + 4050 * months));
        var groceries = page.Locator(".budget-table tr").Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Groceries", Exact = true }) });
        (await groceries.Locator(".available-pill").InnerTextAsync()).ShouldBe(BudgetFacts.Money(40 * months));
    }

    private static async Task VerifyRegisterAsync(IPage page, string path, int months)
    {
        await page.GotoAsync($"{path}/accounts");
        await page.Locator(".workspace[data-interactive='true']").WaitForAsync();
        await page.Locator(".pagination > span").Filter(new() { HasText = $"Page 1 · {months * 200} transactions" }).WaitForAsync();
        (await page.Locator(".pagination > span").InnerTextAsync()).ShouldBe($"Page 1 · {months * 200} transactions");
        (await page.Locator(".register-table tbody tr").CountAsync()).ShouldBe(50);
        var firstRow = await page.Locator(".register-table tbody tr").First.InnerTextAsync();
        await Button(page, "Next").ClickAsync();
        await page.Locator(".pagination > span").Filter(new() { HasText = $"Page 2 · {months * 200} transactions" }).WaitForAsync();
        string.Equals(await page.Locator(".register-table tbody tr").First.InnerTextAsync(), firstRow, StringComparison.Ordinal).ShouldBeFalse();
        var search = page.Locator("fluent-field").Filter(new() { Has = page.GetByText("Search transactions", new() { Exact = true }) }).Locator("input");
        await search.FillAsync("Salary");
        await page.Locator(".pagination > span").Filter(new() { HasText = $"Page 1 · {months * 2} transactions" }).WaitForAsync();
        (await page.Locator(".register-table tbody tr").CountAsync()).ShouldBe(months * 2);
        await search.FillAsync("");
        await page.Locator(".date-filters summary").ClickAsync();
        var first = HouseholdScenario.Start.AddMonths(months - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await page.Locator("#register-from").FillAsync(first);
        await page.Locator("#register-to").FillAsync(HouseholdScenario.End(months).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await page.Locator(".pagination > span").Filter(new() { HasText = "Page 1 · 200 transactions" }).WaitForAsync();
    }

    private static async Task VerifyReportsAsync(IPage page, string path, int months)
    {
        await page.GotoAsync($"{path}/reports?from=2026-01-01&to={HouseholdScenario.End(months):yyyy-MM-dd}");
        await page.GetByRole(AriaRole.Heading, new() { Name = "Reflect", Exact = true }).WaitForAsync();
        (await Stat(page, "Income").InnerTextAsync()).ShouldBe(BudgetFacts.Money(8400 * months));
        (await Stat(page, "Spending").InnerTextAsync()).ShouldBe(BudgetFacts.Money(3920 * months));
        (await Stat(page, "Net worth").InnerTextAsync()).ShouldBe(BudgetFacts.Money(16400 + 4680 * months));
        (await page.Locator(".spending-row").Filter(new() { HasText = "Groceries" }).Locator("strong").InnerTextAsync()).ShouldBe(BudgetFacts.Money(810 * months));
        (await page.Locator(".report-section table").First.Locator("tbody tr").CountAsync()).ShouldBe(months);
        // Exercise the static GET form as well as the direct route.
        await page.GetByLabel("From", new() { Exact = true }).FillAsync(HouseholdScenario.Start.AddMonths(months - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await page.GetByRole(AriaRole.Button, new() { Name = "Update reports", Exact = true }).ClickAsync();
        await Stat(page, "Income").Filter(new() { HasText = "$8,400.00" }).WaitForAsync();
        (await Stat(page, "Spending").InnerTextAsync()).ShouldBe("$3,920.00");
    }

    private static ILocator Stat(IPage page, string name) => page.Locator(".stat")
        .Filter(new() { Has = page.GetByText(name, new() { Exact = true }) }).Locator("strong");
    private static ILocator Button(IPage page, string label) => page.Locator("fluent-button")
        .Filter(new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(label)}\\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)) });

    private sealed class HouseholdApi(IAPIRequestContext request, string token, Guid id)
    {
        private long _version;
        private Guid _paymentCategory;
        public string Path => $"/plans/{id}";

        public static async Task<HouseholdApi> CreateAsync(IAPIRequestContext request)
        {
            var tokenResponse = await request.GetAsync("/api/plans/token");
            var token = (await tokenResponse.JsonAsync()).ShouldNotBeNull().GetProperty("token").GetString().ShouldNotBeNull();
            await tokenResponse.DisposeAsync();
            var response = await request.PostAsync("/api/plans/", new()
            {
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = token },
                DataObject = new { name = "Three month browser household", starterCategories = false },
            });
            response.Status.ShouldBe(201, await response.TextAsync());
            var id = (await response.JsonAsync()).ShouldNotBeNull().GetGuid();
            await response.DisposeAsync();
            return new(request, token, id);
        }

        public async Task CreateCatalogAsync()
        {
            var fixture = HouseholdScenario.Create(1, 200);
            foreach (var account in fixture.Accounts)
            {
                await PutAsync($"accounts/{account.Id}", new SaveAccount(_version, account));
            }
            await PutAsync($"groups/{fixture.Groups[0].Id}", new SaveGroup(_version, fixture.Groups[0]));
            foreach (var category in fixture.Categories.Where(item => item.CreditAccountId is null))
            {
                await PutAsync($"categories/{category.Id}", new SaveCategory(_version, category));
            }
            _paymentCategory = (await ReadAsync()).Categories.Single(item => item.CreditAccountId == HouseholdScenario.Id(3)).Id;
        }

        public async Task AddMonthAsync(int months, CancellationToken cancellationToken)
        {
            var fixture = HouseholdScenario.Create(months, 200);
            var month = HouseholdScenario.Start.AddMonths(months - 1);
            foreach (var allocation in fixture.Allocations.Where(item => item.Month == month))
            {
                var category = allocation.CategoryId == HouseholdScenario.Id(108) ? _paymentCategory : allocation.CategoryId;
                await PutAsync("assignments", new AssignMoney(_version, category, month, allocation.Amount));
            }
            foreach (var entry in fixture.Transactions.Where(item => BudgetFacts.Month(item.Date) == month))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PutAsync($"transactions/{entry.Id}", new SaveTransaction(_version, entry));
            }
        }

        public async Task<PlanSnapshot> ReadAsync()
        {
            var response = await request.GetAsync($"/api{Path}");
            response.Status.ShouldBe(200);
            var snapshot = JsonSerializer.Deserialize<PlanSnapshot>(await response.TextAsync(), JsonSerializerOptions.Web).ShouldNotBeNull();
            await response.DisposeAsync();
            return snapshot;
        }

        private async Task PutAsync(string resource, PlanCommand command)
        {
            var response = await request.PutAsync($"/api{Path}/{resource}", new()
            {
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = token, ["Content-Type"] = "application/json" },
                Data = JsonSerializer.Serialize(command, command.GetType(), JsonSerializerOptions.Web),
            });
            response.Status.ShouldBe(200, await response.TextAsync());
            _version = (await response.JsonAsync()).ShouldNotBeNull().GetProperty("version").GetInt64();
            await response.DisposeAsync();
        }
    }
}
