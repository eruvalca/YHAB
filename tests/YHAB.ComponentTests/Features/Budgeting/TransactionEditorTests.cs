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
    [Fact]
    public async Task EditorSavesATransferWithoutClearingOrApprovalInputsAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var source = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var destination = new AccountData(Guid.NewGuid(), "Savings", AccountKind.Savings, 500, today, false, "");
        var transaction = new TransactionData(Guid.NewGuid(), source.Id, today, "Transfer", "", -20,
            destination.Id, "", [], RepeatFrequency.None);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Household", "", today, 7, [source, destination], [], [], [], [transaction], false, false, []) { Today = today };
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.Payees, []).Add(item => item.Transaction, transaction)
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnClose, () => { }));

        component.FindAll("fluent-checkbox").ShouldBeEmpty();
        component.FindAll(".notice").ShouldBeEmpty();
        var save = component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save transaction", StringComparison.Ordinal));
        save.HasAttribute("disabled").ShouldBeFalse();
        await save.ClickAsync();
        var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
        saved.Amount.ShouldBe(-20);
        saved.TransferAccountId.ShouldBe(destination.Id);
        saved.Payee.ShouldBe("Transfer");
    }
}
