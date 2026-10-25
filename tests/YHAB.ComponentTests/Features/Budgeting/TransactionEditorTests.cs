using System.Diagnostics.CodeAnalysis;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
        var categoryError = component.Find("#transaction-category-error");
        categoryError.TextContent.ShouldBe("An outflow needs a spending category. Choose one, or switch the direction to Inflow.");
        categoryError.Closest(".editor-surface").ShouldBe(category.Closest(".editor-surface"));
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
        component.FindAll("[role='alert']").Any(item => item.TextContent.Contains("Money leaving your budget", StringComparison.Ordinal)).ShouldBeTrue();
        SaveButton(component).HasAttribute("disabled").ShouldBeTrue();

        var option = split.QuerySelector("fluent-option[text='Groceries']").ShouldNotBeNull();
        await split.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = option.Id });
        component.FindAll("[role='alert']").ShouldBeEmpty();
        SaveButton(component).HasAttribute("disabled").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Outflow", false)]
    [InlineData("Inflow", true)]
    public async Task TrackingSourceTransferUsesBudgetSideCategoryRequirementAsync(string direction, bool requiresCategory)
    {
        await using var context = CreateContext();
        var plan = CreateTrackingTransferPlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        var transfer = component.FindAll("fluent-dropdown")[2];
        var checking = transfer.QuerySelector("fluent-option[text='Checking']").ShouldNotBeNull();
        await transfer.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = checking.Id });
        if (!string.Equals(direction, "Outflow", StringComparison.Ordinal))
        {
            var directionSelect = component.FindAll("fluent-dropdown")[0];
            var inflow = directionSelect.QuerySelector("fluent-option[text='Inflow']").ShouldNotBeNull();
            await directionSelect.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = inflow.Id });
        }

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "10" });
        var category = component.FindAll("fluent-dropdown")[3];
        if (requiresCategory)
        {
            category.QuerySelector("fluent-option[text='Inflow: Ready to assign']").ShouldBeNull();
            component.Find("#transaction-category-error").TextContent
                .ShouldBe("Money leaving your budget needs a spending category.");
            SaveButton(component).HasAttribute("disabled").ShouldBeTrue();
            var option = category.QuerySelector("fluent-option[text='Groceries']").ShouldNotBeNull();
            await category.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = option.Id });
            SaveButton(component).HasAttribute("disabled").ShouldBeFalse();
            await SaveButton(component).ClickAsync();
            var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
            saved.Amount.ShouldBe(10m);
            saved.Splits.Single().CategoryId.ShouldBe(plan.Categories[0].Id);
        }
        else
        {
            category.QuerySelector("fluent-option[text='Inflow: Ready to assign']").ShouldNotBeNull();
            component.FindAll("#transaction-category-error").ShouldBeEmpty();
            SaveButton(component).HasAttribute("disabled").ShouldBeFalse();
            await SaveButton(component).ClickAsync();
            var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
            saved.Amount.ShouldBe(-10m);
            saved.Splits.Single().CategoryId.ShouldBeNull();
        }
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

    [Fact]
    public async Task EnterInTheAmountFieldSavesTheTransactionAsync()
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

        // The browser script uses the marker to commit typed text before Enter.
        component.Find(".amount-entry").Closest("[data-enter-saves]").ShouldNotBeNull();
        await component.Find(".amount-entry").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        commands.Single().ShouldBeOfType<SaveTransaction>().Transaction.Amount.ShouldBe(12.50m);
    }

    [Fact]
    public async Task EnterInThePayeeFieldSavesTheTypedPayeeAsync()
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

        var payee = component.FindAll("fluent-text-input")[0];
        payee.Closest("[data-enter-saves]").ShouldNotBeNull();
        await payee.ChangeAsync(new() { Value = "Corner shop" });
        await payee.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        commands.Single().ShouldBeOfType<SaveTransaction>().Transaction.Payee.ShouldBe("Corner shop");
    }

    [Fact]
    public async Task EnterDoesNotSaveAnIncompleteTransactionAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "12.50" });

        // The outflow has no spending category yet, so Enter leaves the draft alone.
        await component.Find(".amount-entry").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        commands.ShouldBeEmpty();
        SaveButton(component).HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task InflowRetainsARetiredCategoryAsInvalidAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var retired = Guid.CreateVersion7();
        var transaction = new TransactionData(Guid.CreateVersion7(), plan.Accounts[0].Id, plan.Today, "Refund", "", 12, null, "",
            [new SplitData(Guid.CreateVersion7(), retired, 12, "")]);
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.Transaction, transaction)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnClose, () => { }));

        component.Find("#transaction-category-error").TextContent
            .ShouldBe("That category is no longer available. Choose another category.");
        SaveButton(component).HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task InflowSplitsRetainARetiredCategoryAsInvalidAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var retired = Guid.CreateVersion7();
        var transaction = new TransactionData(Guid.CreateVersion7(), plan.Accounts[0].Id, plan.Today, "Refund", "", 20, null, "",
        [
            new SplitData(Guid.CreateVersion7(), Guid.Empty, 10, ""),
            new SplitData(Guid.CreateVersion7(), retired, 10, ""),
        ]);
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.Transaction, transaction)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnClose, () => { }));

        component.FindAll("[role='alert']").Count(item => string.Equals(item.TextContent, "That category is no longer available. Choose another category.", StringComparison.Ordinal))
            .ShouldBe(1);
        SaveButton(component).HasAttribute("disabled").ShouldBeTrue();
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

    private static PlanSnapshot CreateTrackingTransferPlan()
    {
        var today = new DateOnly(2026, 10, 3);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        return new PlanSnapshot(Guid.NewGuid(), "Household", "", today, 7,
            [new AccountData(Guid.NewGuid(), "Brokerage", AccountKind.Asset, 0, today, false, ""),
             new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 0, today, false, "")], [group],
            [new CategoryData(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null)], [], [], false, false, [])
        { Today = today };
    }

    private static IElement SaveButton(IRenderedComponent<TransactionEditor> component)
        => component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save transaction", StringComparison.Ordinal));
}
