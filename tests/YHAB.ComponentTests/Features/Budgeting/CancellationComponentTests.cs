using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Components;
using YHAB.UI.Features.Budgeting.Pages;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CancellationComponentTests
{
    [Fact]
    public async Task MonthTimeoutExplainsRecoveryAndDoesNotShowOldAmountsAsync()
    {
        await using var context = new BunitContext();
        Configure(context);
        var plan = Plan();
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.LoadMonth,
                (_, _) => Task.FromException<BudgetMonth>(new TaskCanceledException("HTTP timeout"))));

        await component.WaitForAssertionAsync(() => component.Markup.ShouldContain("Loading this month timed out. Refresh your plan to try again."));
        component.FindAll(".hero-amount").ShouldBeEmpty();
    }

    [Fact]
    public async Task SsrRequestAbortsReportReadAndSkipsFollowingQueryAsync()
    {
        await using var context = new BunitContext();
        Configure(context);
        using var request = new CancellationTokenSource();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var budgets = Substitute.For<IBudgetClient>();
        CancellationToken received = default;
        budgets.ReadViewAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            received = call.Arg<CancellationToken>();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, received); }
            finally { stopped.TrySetResult(); }
            return new PlanView(Plan(), [], false);
        });
        context.Services.AddSingleton(budgets);
        var component = context.Render<Reports>(parameters => parameters.Add(item => item.PlanId, Guid.NewGuid())
            .AddCascadingValue("RequestAborted", request.Token));
        received.CanBeCanceled.ShouldBeTrue();

        await request.CancelAsync();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        received.IsCancellationRequested.ShouldBeTrue();
        budgets.ReceivedCalls().ShouldNotContain(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadReportsAsync), StringComparison.Ordinal));
        component.FindAll("[role='alert']").ShouldBeEmpty();
    }

    [Fact]
    public async Task ComponentDisposalCancelsOwnedWorkWithoutCancelingRequestAsync()
    {
        await using var context = new BunitContext();
        Configure(context);
        using var request = new CancellationTokenSource();
        var budgets = Substitute.For<IBudgetClient>();
        var pending = new TaskCompletionSource<IReadOnlyList<PlanSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns(call => { received = call.Arg<CancellationToken>(); return pending.Task; });
        context.Services.AddSingleton(budgets);
        var component = context.Render<Plans>(parameters => parameters.AddCascadingValue("RequestAborted", request.Token));
        var instance = component.Instance;

        component.Dispose();
        await instance.DisposeAsync();

        received.IsCancellationRequested.ShouldBeTrue();
        request.IsCancellationRequested.ShouldBeFalse();
        pending.SetResult([new(Guid.NewGuid(), "Late plan", "", 0)]);
        await context.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
        budgets.ReceivedCalls().Count(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ListAsync), StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task SupersededRegisterReadIsCanceledAndCannotLoadPayeesOrReplaceRowsAsync()
    {
        await using var context = new BunitContext();
        Configure(context);
        var plan = Plan();
        var pending = new TaskCompletionSource<RegisterPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        var payeeReads = 0;
        Task<RegisterPage> LoadAsync(RegisterQuery query, CancellationToken cancellationToken)
        {
            tokens.Add(cancellationToken);
            return tokens.Count == 2 ? pending.Task : Task.FromResult(new RegisterPage(plan.Version, [], 0, null));
        }
        Task<IReadOnlyList<string>> PayeesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payeeReads++;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
        var component = context.Render<AccountRegister>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, LoadAsync).Add(item => item.LoadPayees, PayeesAsync));

        var olderEvent = component.Find("#register-from").ChangeAsync(new ChangeEventArgs { Value = "2026-10-01" });
        await component.WaitForAssertionAsync(() => tokens.Count.ShouldBe(2));
        component.Render(parameters => parameters.Add(item => item.AccountId, plan.Accounts[0].Id));
        await component.WaitForAssertionAsync(() => tokens.Count.ShouldBe(3));
        tokens[1].IsCancellationRequested.ShouldBeTrue();
        tokens[2].IsCancellationRequested.ShouldBeFalse();
        var stale = new TransactionData(Guid.NewGuid(), plan.Accounts[0].Id, plan.Today, "Stale payee", "", -10, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", []);
        pending.SetResult(new(plan.Version, [new(stale, 990)], 1, null));
        await olderEvent;

        component.Markup.ShouldNotContain("Stale payee");
        payeeReads.ShouldBe(2);
        component.FindAll("[role='alert']").ShouldBeEmpty();
    }

    [Fact]
    public async Task SupersededMonthReadCannotOverwriteNewMonthAsync()
    {
        await using var context = new BunitContext();
        Configure(context);
        var plan = Plan();
        var pending = new TaskCompletionSource<BudgetMonth>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        var reads = 0;
        Task<BudgetMonth> LoadAsync(DateOnly month, CancellationToken cancellationToken)
        {
            tokens.Add(cancellationToken);
            return ++reads == 2 ? pending.Task : Task.FromResult(BudgetCalculator.Calculate(plan, month, plan.Today));
        }
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.LoadMonth, LoadAsync));
        var november = component.Find("fluent-button[aria-label='Next month']").ClickAsync();
        await component.WaitForAssertionAsync(() => tokens.Count.ShouldBe(2));
        await component.Find("fluent-button[aria-label='Next month']").ClickAsync();
        tokens[1].IsCancellationRequested.ShouldBeTrue();
        tokens[2].IsCancellationRequested.ShouldBeFalse();
        pending.SetResult(BudgetCalculator.Calculate(plan with { Accounts = [] }, new(2026, 11, 1), plan.Today));
        await november;

        component.Find(".month-label").TextContent.ShouldBe("December 2026");
        component.Find(".hero-amount").TextContent.ShouldBe("$1,000.00");
        component.FindAll("[role='alert']").ShouldBeEmpty();
    }

    private static void Configure(BunitContext context)
    {
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PlanSnapshot Plan()
    {
        var today = new DateOnly(2026, 10, 2);
        return new(Guid.NewGuid(), "Cancellation plan", "", today, 7,
            [new(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "")], [], [], [], [], false, false, [])
        { Today = today };
    }
}
