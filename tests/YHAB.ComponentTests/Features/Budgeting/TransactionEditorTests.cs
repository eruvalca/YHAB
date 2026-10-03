using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class TransactionEditorTests
{
    [Theory]
    [InlineData(ClearingState.Reconciled, ClearingState.Uncleared, true)]
    [InlineData(ClearingState.Cleared, ClearingState.Reconciled, true)]
    [InlineData(ClearingState.Uncleared, ClearingState.Reconciled, false)]
    [InlineData(ClearingState.Cleared, ClearingState.Uncleared, true)]
    public async Task EditorExplainsReconciliationLockAndShowsActualClearingStateAsync(ClearingState state, ClearingState transferState, bool cleared)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var source = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var destination = new AccountData(Guid.NewGuid(), "Savings", AccountKind.Savings, 500, today, false, "");
        var transaction = new TransactionData(Guid.NewGuid(), source.Id, today, "Transfer", "", -20,
            destination.Id, state, transferState, false, "", [], RepeatFrequency.None);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Household", "", today, 7, [source, destination], [], [], [], [transaction], false, false, []) { Today = today };
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.Transaction, transaction)
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnClose, () => { }));

        var locked = state == ClearingState.Reconciled || transferState == ClearingState.Reconciled;
        component.FindComponent<FluentCheckbox>().Instance.Value.ShouldBe(cleared);
        component.FindComponent<FluentCheckbox>().Instance.Disabled.ShouldBe(locked);
        var save = component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save transaction", StringComparison.Ordinal));
        save.HasAttribute("disabled").ShouldBe(locked);
        if (locked)
        {
            component.Find(".notice").TextContent.ShouldContain("Mark uncleared / unreconcile");
            commands.ShouldBeEmpty();
        }
        else
        {
            component.FindAll(".notice").ShouldBeEmpty();
            await save.ClickAsync();
            var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
            saved.State.ShouldBe(ClearingState.Cleared);
            saved.TransferState.ShouldBe(ClearingState.Uncleared);
            saved.Amount.ShouldBe(-20);
            saved.TransferAccountId.ShouldBe(destination.Id);
        }
    }
}
