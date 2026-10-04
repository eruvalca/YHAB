using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Endpoints;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HttpCancellationTests
{
    [Fact]
    public async Task AbortedHttpRequestCancelsDatabaseWorkAndNextRequestSucceedsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var interceptor = new PendingQuery();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: interceptor);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton(database.Store);
        builder.Services.AddSingleton(database.Queries);
        await using var app = builder.Build();
        app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner-a")], "Cancellation test"));
            return next(context);
        });
        app.UseAuthorization();
        BudgetEndpoints.Map(app);
        await app.StartAsync(token);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            interceptor.Arm();
            var pending = http.GetAsync(new Uri("/api/plans", UriKind.Relative), request.Token);
            await interceptor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

            await request.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => pending);
            await interceptor.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            interceptor.Token.CanBeCanceled.ShouldBeTrue();
            interceptor.Token.IsCancellationRequested.ShouldBeTrue();
            using var response = await http.GetAsync(new Uri("/api/plans", UriKind.Relative), token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(token)).ShouldBe("[]");
        }
        finally
        {
            // Teardown must still run when the test/request token was canceled.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StopAsync(cleanup.Token);
        }
    }

    private sealed class PendingQuery : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Token = cancellationToken;
                Entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                finally { Stopped.TrySetResult(); }
            }
            return result;
        }
    }
}
