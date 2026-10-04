using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Npgsql;
using Shouldly;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.PlaywrightTests;

internal sealed class HttpLoadFixture(Guid id, Guid[] accounts, Guid[] categories, Guid[] templates, DateOnly today, string connectionString)
{
    public Guid Id { get; } = id;
    public Guid[] Categories { get; } = categories;
    public DateOnly Today { get; } = today;
    public string Path => $"/api/plans/{Id}";

    public static async Task<HttpLoadFixture> CreateAsync(IAPIRequestContext request, string csrf, string connectionString, CancellationToken token,
        bool includeRecurring = true, decimal assigned = 1)
    {
        var response = await request.PostAsync("/api/plans/", new()
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-CSRF-TOKEN"] = csrf },
            DataObject = new { name = "Independent process load", starterCategories = false },
        });
        response.Status.ShouldBe(201, await response.TextAsync());
        var id = (await response.JsonAsync()).ShouldNotBeNull().GetGuid();
        await response.DisposeAsync();
        var empty = await ReadAsync<PlanSnapshot>(request, $"/api/plans/{id}");
        var fixture = new HttpLoadFixture(id, [Guid.CreateVersion7(), Guid.CreateVersion7()],
            Enumerable.Range(0, 8).Select(_ => Guid.CreateVersion7()).ToArray(), [Guid.CreateVersion7(), Guid.CreateVersion7()], empty.Today, connectionString);
        await fixture.SeedAsync(includeRecurring, assigned, token);
        return fixture;
    }

    public static async Task<T> ReadAsync<T>(IAPIRequestContext request, string path)
    {
        var response = await request.GetAsync(path, new() { Timeout = 15000 });
        try
        {
            response.Status.ShouldBe(200, await response.TextAsync());
            var result = JsonSerializer.Deserialize<T>(await response.TextAsync(), JsonSerializerOptions.Web);
            ((object?)result).ShouldNotBeNull();
            return result;
        }
        finally { await response.DisposeAsync(); }
    }

    public async Task<(bool Loaded, int Conflicts)> RefreshMonthAsync(IAPIRequestContext request, int category, int assigned)
    {
        // Match Workspace's bounded read-only recovery. Report the conflicts and
        // exhausted attempts separately from successfully displayed refreshes.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var month = BudgetFacts.Month(Today);
            var path = string.Create(CultureInfo.InvariantCulture, $"{Path}/workspace?month={month:yyyy-MM-dd}");
            var response = await request.GetAsync(path, new() { Timeout = 15000 });
            try
            {
                if (response.Status == 409) { continue; }
                response.Status.ShouldBe(200, await response.TextAsync());
                var result = JsonSerializer.Deserialize<PlanMonthView>(await response.TextAsync(), JsonSerializerOptions.Web).ShouldNotBeNull();
                var view = result.View;
                var budget = result.Month;
                budget.Month.ShouldBe(month);
                budget.Categories.Count.ShouldBe(8);
                budget.Categories.Single(item => item.Category.Id == Categories[category]).Assigned.ShouldBe(assigned);
                (budget.ReadyToAssign + budget.Available).ShouldBe(view.Balances.Sum(item => item.Working));
                return (true, attempt);
            }
            finally { await response.DisposeAsync(); }
        }
        return (false, 3);
    }

    private async Task SeedAsync(bool includeRecurring, decimal assigned, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "BudgetAccount" ("PlanId", "Id", "Name", "Kind", "OpeningBalance", "OpenedOn", "Closed", "Notes", "InterestRate", "MinimumPayment")
            SELECT @id, x, 'Load account', 0, 1000000, @start, false, '', 0, 0 FROM unnest(@accounts) x;
            INSERT INTO "BudgetGroup" ("PlanId", "Id", "Name", "SortOrder", "Hidden") VALUES (@id, @group, 'Load categories', 0, false);
            INSERT INTO "BudgetCategory" ("PlanId", "Id", "GroupId", "Name", "Notes", "SortOrder", "Hidden", "TargetCadence", "TargetAmount", "TargetStartMonth", "TargetRepeatMonths", "TargetWeekday")
            SELECT @id, @categories[n], @group, 'Editor ' || n, '', n, false, 0, 0, @start, 0, 0 FROM generate_series(1, 8) n;
            INSERT INTO "BudgetAllocation" ("PlanId", "CategoryId", "Month", "Amount", "Snoozed")
            SELECT @id, x, @month, @assigned, false FROM unnest(@categories) x;
            INSERT INTO "BudgetTransaction" ("PlanId", "Id", "AccountId", "Date", "Payee", "Memo", "Amount", "State", "TransferState", "NeedsApproval", "Flag", "Repeat", "Occurrence", "Sequence")
            SELECT @id, gen_random_uuid(), @accounts[1 + n % 2], @today - n % 730, 'Load ledger', '',
                CASE WHEN n % 10 = 0 THEN 100 ELSE -1 END, 0, 0, false, '', 0, 0, n FROM generate_series(1, 100000) n;
            INSERT INTO "BudgetTransaction" ("PlanId", "Id", "AccountId", "Date", "Payee", "Memo", "Amount", "State", "TransferState", "NeedsApproval", "Flag", "Repeat", "AnchorDate", "Occurrence", "Sequence")
            SELECT @id, @templates[n], @accounts[1], @today - 364, 'Daily load schedule', '', -1, 0, 0, false, '', @daily, @today - 364, 0, 100000 + n FROM generate_series(1, 2) n WHERE @includeRecurring;
            INSERT INTO "BudgetSplit" ("PlanId", "Id", "TransactionId", "CategoryId", "Amount", "Memo")
            SELECT "PlanId", gen_random_uuid(), "Id", CASE WHEN "Amount" < 0 THEN @categories[1 + ("Sequence" % 8)::int] ELSE NULL END, "Amount", ''
            FROM "BudgetTransaction" WHERE "PlanId" = @id;
            UPDATE "BudgetPlans" SET "Version" = 1, "NextSequence" = 100002 WHERE "Id" = @id;
            """;
        command.Parameters.AddWithValue("id", Id);
        command.Parameters.AddWithValue("accounts", accounts);
        command.Parameters.AddWithValue("categories", Categories);
        command.Parameters.AddWithValue("templates", templates);
        command.Parameters.AddWithValue("group", Guid.CreateVersion7());
        command.Parameters.AddWithValue("start", Today.AddDays(-729));
        command.Parameters.AddWithValue("today", Today);
        command.Parameters.AddWithValue("month", BudgetFacts.Month(Today));
        command.Parameters.AddWithValue("daily", (int)RepeatFrequency.Daily);
        command.Parameters.AddWithValue("includeRecurring", includeRecurring);
        command.Parameters.AddWithValue("assigned", assigned);
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task VerifyAsync(IAPIRequestContext request, IReadOnlyList<Guid> receipts, int[] completed, CancellationToken token)
    {
        var view = await ReadAsync<PlanView>(request, $"{Path}/view");
        view.HasDueRecurring.ShouldBeFalse();
        view.Balances.Single(item => item.AccountId == accounts[0]).Working.ShouldBe(1_959_270);
        view.Balances.Single(item => item.AccountId == accounts[1]).Working.ShouldBe(950_000);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM "BudgetReceipt" WHERE "PlanId" = @id AND "OperationId" = ANY(@receipts);
            SELECT "CategoryId", "Amount" FROM "BudgetAllocation" WHERE "PlanId" = @id AND "Month" = @month;
            SELECT "SourceTemplateId", "ScheduledDate", "Amount" FROM "BudgetTransaction" WHERE "PlanId" = @id AND "SourceTemplateId" IS NOT NULL ORDER BY "SourceTemplateId", "ScheduledDate";
            SELECT COUNT(*) FROM "BudgetHistory" WHERE "PlanId" = @id;
            """;
        command.Parameters.AddWithValue("id", Id);
        command.Parameters.AddWithValue("receipts", receipts.ToArray());
        command.Parameters.AddWithValue("month", BudgetFacts.Month(Today));
        await using var reader = await command.ExecuteReaderAsync(token);
        (await reader.ReadAsync(token)).ShouldBeTrue();
        reader.GetInt64(0).ShouldBe(receipts.Count);
        (await reader.NextResultAsync(token)).ShouldBeTrue();
        var assigned = new Dictionary<Guid, decimal>();
        while (await reader.ReadAsync(token)) { assigned.Add(reader.GetGuid(0), reader.GetDecimal(1)); }
        assigned.Count.ShouldBe(8);
        for (var index = 0; index < 8; index++) { assigned[Categories[index]].ShouldBe(completed[index] + 1); }
        (await reader.NextResultAsync(token)).ShouldBeTrue();
        var occurrences = new List<(Guid Template, DateOnly Date, decimal Amount)>();
        while (await reader.ReadAsync(token)) { occurrences.Add((reader.GetGuid(0), await reader.GetFieldValueAsync<DateOnly>(1, token), reader.GetDecimal(2))); }
        occurrences.OrderBy(item => item.Template).ThenBy(item => item.Date).ShouldBe(templates.SelectMany(template =>
            Enumerable.Range(0, 365).Select(day => (Template: template, Date: Today.AddDays(day - 364), Amount: -1m))).OrderBy(item => item.Template).ThenBy(item => item.Date));
        (await reader.NextResultAsync(token)).ShouldBeTrue();
        (await reader.ReadAsync(token)).ShouldBeTrue();
        reader.GetInt64(0).ShouldBe(Math.Min(50, receipts.Count));
        view.Catalog.Version.ShouldBe(1 + receipts.Count + 6);
    }
}
