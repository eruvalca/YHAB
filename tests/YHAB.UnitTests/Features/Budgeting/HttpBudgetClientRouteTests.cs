using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery));
            if (request.RequestUri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                return Task.FromResult(Respond(HttpStatusCode.OK, """{"token":"csrf"}"""));
            }
            if (request.Method == HttpMethod.Delete)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(Respond(HttpStatusCode.OK, """{"ownerName":"b@example.test","isOwner":false,"collaborators":[]}"""));
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
