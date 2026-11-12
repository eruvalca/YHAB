using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Client.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HttpBudgetClientRouteTests
{
    [Fact]
    public async Task LifecycleRequestsUseTheirDocumentedRoutesAsync()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://budget.example.test/") };
        var client = new HttpBudgetClient(http);
        var planId = Guid.NewGuid();

        await client.DeleteAsync(planId, "My plan", TestContext.Current.CancellationToken);
        var summary = await client.TransferOwnershipAsync(planId, "owner-b", TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(
        [
            (HttpMethod.Get, "/api/plans/token"),
            (HttpMethod.Delete, $"/api/plans/{planId}?confirm=My%20plan"),
            (HttpMethod.Post, $"/api/plans/{planId}/sharing/owner-b/owner"),
        ]);
        summary.OwnerName.ShouldBe("b@example.test");
        summary.IsOwner.ShouldBeFalse();
    }

    [Fact]
    public async Task CommandsSerializeWithTheirConcreteContractAsync()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://budget.example.test/") };
        var client = new HttpBudgetClient(http);
        var planId = Guid.NewGuid();
        var assignment = new AssignMoney(11, Guid.NewGuid(), new DateOnly(2026, 3, 1), 25.50m);
        var rename = new UpdatePlan(12, "Household", "");

        await client.ExecuteAsync(planId, assignment, TestContext.Current.CancellationToken);
        await client.ExecuteAsync(planId, rename, TestContext.Current.CancellationToken);

        // The endpoint contracts are concrete commands, so the source-generated metadata must
        // produce the same payload as reflection-based serialization of the runtime type.
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        handler.Bodies.ShouldBe(
        [
            JsonSerializer.Serialize(assignment, assignment.GetType(), web),
            JsonSerializer.Serialize(rename, rename.GetType(), web),
        ]);
    }

    [Fact]
    public async Task ReadsDeserializeServerFormattedNestedPayloadsAsync()
    {
        var planId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var created = new DateOnly(2026, 2, 3);
        var month = new DateOnly(2026, 2, 1);
        var snapshot = new PlanSnapshot(planId, "Household", "", created, 7, [], [], [], [], [], false, true, [])
        {
            Today = new DateOnly(2026, 2, 10),
            OwnerName = "b@example.test",
        };
        var monthView = new PlanMonthView(
            new PlanView(snapshot, [new AccountBalance(Guid.NewGuid(), 1234.56m)], true),
            new BudgetMonth(month, 100.25m, 200.50m, -50.75m, 49.75m, 0m, 0m, 0m, []));
        var transaction = new TransactionData(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 2, 4), "Shop", "", -12.34m, null, "", [],
            RepeatFrequency.Monthly, created, 2, null, new DateOnly(2026, 3, 4));
        var register = new RegisterPage(7, [new RegisterRow(transaction, 99.99m)], 1,
            new RegisterCursor(new DateOnly(2026, 2, 4), 2, transaction.Id, "Shop", -12.34m));
        var report = new ReportView(7, [new MonthReport(month, 500m, 120m, 1000m, 250m)],
            [new CategorySpending(categoryId, "Food", 120m)], new Dictionary<Guid, decimal> { [categoryId] = 120m });

        // Serialize with the server's Web defaults so the client's source-generated metadata
        // is read against the format the API actually produces.
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var handler = new RecordingHandler
        {
            Responses =
            [
                ($"/api/plans/{planId}", JsonSerializer.Serialize(snapshot, web)),
                ($"/api/plans/{planId}/workspace", JsonSerializer.Serialize(monthView, web)),
                ($"/api/plans/{planId}/register", JsonSerializer.Serialize(register, web)),
                ($"/api/plans/{planId}/reports", JsonSerializer.Serialize(report, web)),
            ],
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://budget.example.test/") };
        var client = new HttpBudgetClient(http);
        var token = TestContext.Current.CancellationToken;

        var readSnapshot = await client.ReadAsync(planId, token);
        readSnapshot.OwnerName.ShouldBe("b@example.test");
        readSnapshot.CreatedOn.ShouldBe(created);
        readSnapshot.Today.ShouldBe(new DateOnly(2026, 2, 10));
        readSnapshot.CanRedo.ShouldBeTrue();

        var readMonth = await client.ReadWorkspaceAsync(planId, null, token);
        readMonth.Month.ReadyToAssign.ShouldBe(100.25m);
        readMonth.Month.Activity.ShouldBe(-50.75m);
        readMonth.View.Balances.Single().Working.ShouldBe(1234.56m);
        readMonth.View.HasDueRecurring.ShouldBeTrue();

        var readRegister = await client.ReadRegisterAsync(planId, new RegisterQuery(), token);
        readRegister.Total.ShouldBe(1);
        var row = readRegister.Rows.Single();
        row.RunningBalance.ShouldBe(99.99m);
        row.Transaction.Amount.ShouldBe(-12.34m);
        row.Transaction.Date.ShouldBe(new DateOnly(2026, 2, 4));
        row.Transaction.Repeat.ShouldBe(RepeatFrequency.Monthly);
        row.Transaction.TransferAccountId.ShouldBeNull();
        row.Transaction.AnchorDate.ShouldBe(created);
        readRegister.Next.ShouldNotBeNull().Sequence.ShouldBe(2);

        var readReport = await client.ReadReportsAsync(planId, month, month, token);
        readReport.Months.Single().NetWorth.ShouldBe(750m);
        readReport.Spending.Single().Amount.ShouldBe(120m);
        readReport.Budgeted[categoryId].ShouldBe(120m);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery)> Calls { get; } = [];

        public List<string> Bodies { get; } = [];

        public List<(string Path, string Body)> Responses { get; init; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery));
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            foreach (var (candidate, json) in Responses)
            {
                if (string.Equals(candidate, request.RequestUri.AbsolutePath, StringComparison.Ordinal))
                {
                    return Respond(HttpStatusCode.OK, json);
                }
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                return Respond(HttpStatusCode.OK, """{"token":"csrf"}""");
            }
            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return Respond(HttpStatusCode.OK, """{"ownerName":"b@example.test","isOwner":false,"collaborators":[]}""");
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
