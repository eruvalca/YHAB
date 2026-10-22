using System.Diagnostics.CodeAnalysis;
using AngleSharp.Dom;
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
    public async Task NewOutflowAsksForASpendingCategoryAndBlocksSaveAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        var category = component.FindAll("fluent-dropdown")[3];
        category.QuerySelector("fluent-option[text='Inflow: Ready to assign']").ShouldBeNull();
        category.GetAttribute("placeholder").ShouldBe("Choose a category");
        category.GetAttribute("aria-invalid").ShouldBe("true");
        category.GetAttribute("aria-describedby").ShouldBe("transaction-category-error");
        component.Find("#transaction-category-error").TextContent
            .ShouldBe("An outflow needs a spending category. Choose one, or switch the direction to Inflow.");
        SaveButton(component).HasAttribute("disabled").ShouldBeTrue();

        var option = category.QuerySelector("fluent-option[text='Groceries']").ShouldNotBeNull();
        await category.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = option.Id });
        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "12.50" });

        component.FindAll("#transaction-category-error").ShouldBeEmpty();
        component.FindAll("fluent-dropdown")[3].GetAttribute("aria-invalid").ShouldBe("false");
        SaveButton(component).HasAttribute("disabled").ShouldBeFalse();
        await SaveButton(component).ClickAsync();

        var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
        saved.Amount.ShouldBe(-12.50m);
        saved.Splits.Single().CategoryId.ShouldBe(plan.Categories[0].Id);
    }

    [Fact]
    public async Task InflowKeepsReadyToAssignAsTheDefaultCategoryAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        var direction = component.FindAll("fluent-dropdown")[0];
        var inflow = direction.QuerySelector("fluent-option[text='Inflow']").ShouldNotBeNull();
        await direction.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = inflow.Id });
        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "12.50" });

        var category = component.FindAll("fluent-dropdown")[3];
        category.QuerySelector("fluent-option[text='Inflow: Ready to assign']").ShouldNotBeNull();
        component.FindAll("#transaction-category-error").ShouldBeEmpty();
        SaveButton(component).HasAttribute("disabled").ShouldBeFalse();
        await SaveButton(component).ClickAsync();

        var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
        saved.Amount.ShouldBe(12.50m);
        saved.Splits.Single().CategoryId.ShouldBeNull();

        var outflow = component.FindAll("fluent-dropdown")[0].QuerySelector("fluent-option[text='Outflow']").ShouldNotBeNull();
        await component.FindAll("fluent-dropdown")[0].TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = outflow.Id });
        component.Find("#transaction-category-error").TextContent
            .ShouldBe("An outflow needs a spending category. Choose one, or switch the direction to Inflow.");
    }

    [Fact]
    public async Task OutflowSplitRequiresACategoryBeforeSavingAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "7.77" });
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Split across categories", StringComparison.Ordinal)).ClickAsync();

        var split = component.FindAll("fluent-dropdown")[3];
        split.QuerySelector("fluent-option[text='Inflow: Ready to assign']").ShouldBeNull();
        component.FindAll("[role='alert']").Any(item => item.TextContent.Contains("outflow split", StringComparison.Ordinal)).ShouldBeTrue();
        SaveButton(component).HasAttribute("disabled").ShouldBeTrue();

        var option = split.QuerySelector("fluent-option[text='Groceries']").ShouldNotBeNull();
        await split.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = option.Id });
        component.FindAll("[role='alert']").ShouldBeEmpty();
        SaveButton(component).HasAttribute("disabled").ShouldBeFalse();
    }

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

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static PlanSnapshot CreatePlan()
    {
        var today = new DateOnly(2026, 10, 3);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        return new PlanSnapshot(Guid.NewGuid(), "Household", "", today, 7,
            [new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 0, today, false, "")], [group],
            [new CategoryData(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null)], [], [], false, false, [])
        { Today = today };
    }

    private static IElement SaveButton(IRenderedComponent<TransactionEditor> component)
        => component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save transaction", StringComparison.Ordinal));
}
