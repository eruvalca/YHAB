using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Pages;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class WorkspaceTests
{
    [Theory]
    [InlineData("Static", false)]
    [InlineData("Server", true)]
    [InlineData("WebAssembly", true)]
    public async Task DelayedSnapshotPostsDueRecurringOnlyWhenInteractiveAsync(string renderer, bool interactive)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.ComponentFactories.AddStub<FluentProviders>();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/YHAB.UI/budget-workspace.js");
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var recurring = new TransactionData(Guid.NewGuid(), account.Id, today, "Salary", "", 2000, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [], RepeatFrequency.Monthly);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Delayed plan", "", today, 7, [account], [], [], [], [recurring], false, false, []) { Today = today };
        var posted = plan with
        {
            Version = 8,
            Transactions =
            [
                recurring with { Date = today.AddMonths(1) },
                recurring with { Id = Guid.NewGuid(), Repeat = RepeatFrequency.None, SourceTemplateId = recurring.Id, ScheduledDate = today },
            ],
        };
        var pendingRead = new TaskCompletionSource<PlanSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ReadAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(pendingRead.Task, Task.FromResult(posted));
        context.Services.AddSingleton(budgets);
        context.Renderer.SetRendererInfo(new RendererInfo(renderer, interactive));

        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        component.Find("[role='status']").TextContent.ShouldBe("Opening your plan…");
        Commands(budgets).ShouldBeEmpty();

        pendingRead.SetResult(plan);
        await component.WaitForAssertionAsync(() => component.Find(".workspace").GetAttribute("data-interactive").ShouldBe(interactive ? "true" : "false"));
        if (interactive)
        {
            await component.WaitForAssertionAsync(() => Commands(budgets).Length.ShouldBe(1));
            var command = Commands(budgets).Single().ShouldBeOfType<PostRecurring>();
            command.Version.ShouldBe(7);
            command.ThroughDate.ShouldBe(today);
        }
        else
        {
            Commands(budgets).ShouldBeEmpty();
        }
        budgets.ReceivedCalls().Count(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadAsync), StringComparison.Ordinal)).ShouldBe(interactive ? 2 : 1);
        await component.WaitForAssertionAsync(() => component.Find(".hero-amount").TextContent.ShouldBe(interactive ? "$3,000.00" : "$1,000.00"));
    }

    private static PlanCommand[] Commands(IBudgetClient budgets) => budgets.ReceivedCalls()
        .Where(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ExecuteAsync), StringComparison.Ordinal))
        .Select(call => (PlanCommand)call.GetArguments()[1]!).ToArray();
}
