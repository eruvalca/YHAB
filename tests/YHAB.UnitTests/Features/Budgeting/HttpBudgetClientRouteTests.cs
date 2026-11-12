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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery)> Calls { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery));
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
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
