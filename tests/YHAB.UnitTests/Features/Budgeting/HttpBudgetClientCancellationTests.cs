using System.Diagnostics.CodeAnalysis;
using System.Net;
using Shouldly;
using Xunit;
using YHAB.Client.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HttpBudgetClientCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAbortsTransportAndStopsAntiforgeryBootstrapAsync(bool write)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new PendingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://budget.example.test/") };
        var client = new HttpBudgetClient(http);
        var pending = write ? client.ExecuteAsync(Guid.NewGuid(), new UndoChange(7), cancellation.Token) : client.ListAsync(cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending);
        handler.Token.IsCancellationRequested.ShouldBeTrue();
        handler.Routes.ShouldBe([write ? "/api/plans/token" : "/api/plans"]);
        handler.Methods.ShouldBe([HttpMethod.Get]);
    }

    [Fact]
    public async Task PreCanceledRequestDoesNotReachTransportAsync()
    {
        using var handler = new PendingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://budget.example.test/") };
        var client = new HttpBudgetClient(http);

        await Should.ThrowAsync<OperationCanceledException>(() => client.ListAsync(new CancellationToken(canceled: true)));

        handler.Routes.ShouldBeEmpty();
    }

    [Fact]
    public void BudgetClientContractsRequireExplicitCancellation()
    {
        foreach (var type in new[] { typeof(IBudgetClient), typeof(HttpBudgetClient), typeof(YHAB.Features.Budgeting.Services.ServerBudgetClient) })
        {
            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            methods.Length.ShouldBe(typeof(IBudgetClient).GetMethods().Length);
            foreach (var method in methods)
            {
                var parameter = method.GetParameters()[^1];
                parameter.ParameterType.ShouldBe(typeof(CancellationToken), method.Name);
                parameter.IsOptional.ShouldBeFalse(method.Name);
            }
        }
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Routes { get; } = [];
        public List<HttpMethod> Methods { get; } = [];
        public CancellationToken Token { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Routes.Add(request.RequestUri!.AbsolutePath);
            Methods.Add(request.Method);
            Entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.OK);
        }
    }
}
