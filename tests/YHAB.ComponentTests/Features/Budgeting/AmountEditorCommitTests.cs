using System.Diagnostics.CodeAnalysis;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AmountEditorCommitTests
{
    [Fact]
    public async Task TransactionEditorCommitsAmountDraftWhenSavedWithoutBlurAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var transaction = new TransactionData(Guid.NewGuid(), plan.Accounts[0].Id, plan.Today, "Market", "", -12.50m,
            null, "", [new(Guid.NewGuid(), plan.Categories[0].Id, -12.50m, "")], RepeatFrequency.None);
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.Transaction, transaction)
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "7.77" });
        await SaveButton(component, "Save transaction").ClickAsync();

        var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
        saved.Amount.ShouldBe(-7.77m);
        saved.Splits.Single().Amount.ShouldBe(-7.77m);
    }

    [Fact]
    public async Task TransactionEditorCommitsSplitDraftWhenSavedWithoutBlurAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "7.77" });
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Split across categories", StringComparison.Ordinal)).ClickAsync();
        var category = component.FindAll("fluent-dropdown")[3];
        var option = category.QuerySelector("fluent-option[text='Groceries']").ShouldNotBeNull();
        await category.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = option.Id });
        await component.FindAll("fluent-text-input")[2].ChangeAsync(new() { Value = "-7.77" });
        await SaveButton(component, "Save transaction").ClickAsync();

        var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
        saved.Amount.ShouldBe(-7.77m);
        saved.Splits.Single().Amount.ShouldBe(-7.77m);
    }

    [Fact]
    public async Task TransactionEditorIgnoresHiddenInvalidSplitWhenSavingBudgetTransferAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var destination = plan.Accounts[0] with { Id = Guid.NewGuid(), Name = "Savings", Kind = AccountKind.Savings };
        plan = plan with { Accounts = [.. plan.Accounts, destination] };
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "20" });
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Split across categories", StringComparison.Ordinal)).ClickAsync();
        await component.FindAll("fluent-text-input")[2].ChangeAsync(new() { Value = "1 / 0" });

        component.FindAll("[role='alert']").Any(item => item.TextContent.Contains("Enter an amount", StringComparison.Ordinal)).ShouldBeTrue();
        SaveButton(component, "Save transaction").HasAttribute("disabled").ShouldBeTrue();
        commands.ShouldBeEmpty();

        var transfer = component.FindAll("fluent-dropdown")[2];
        var option = transfer.QuerySelector("fluent-option[text='Savings']").ShouldNotBeNull();
        await transfer.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = option.Id });

        component.FindAll("[role='alert']").ShouldBeEmpty();
        component.FindAll("fluent-text-input").Count.ShouldBe(2);
        SaveButton(component, "Save transaction").HasAttribute("disabled").ShouldBeFalse();
        await SaveButton(component, "Save transaction").ClickAsync();

        var saved = commands.Single().ShouldBeOfType<SaveTransaction>().Transaction;
        saved.AccountId.ShouldBe(plan.Accounts[0].Id);
        saved.TransferAccountId.ShouldBe(destination.Id);
        saved.Amount.ShouldBe(-20m);
        saved.Splits.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccountEditorCommitsOpeningBalanceDraftWhenSavedWithoutBlurAsync()
    {
        await using var context = CreateContext();
        var commands = new List<PlanCommand>();
        var component = context.Render<AccountEditor>(parameters => parameters.Add(item => item.Plan, CreatePlan())
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "500" });
        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "Race checking" });
        await SaveButton(component, "Save account").ClickAsync();

        commands.Single().ShouldBeOfType<SaveAccount>().Account.OpeningBalance.ShouldBe(500m);
    }

    [Fact]
    public async Task MoveMoneyEditorCommitsAmountDraftWhenSavedWithoutBlurAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<MoveMoneyEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Month, BudgetFacts.Month(plan.Today)).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "25.50" });
        await SaveButton(component, "Move money").ClickAsync();

        commands.Single().ShouldBeOfType<MoveMoney>().Amount.ShouldBe(25.50m);
    }

    [Fact]
    public async Task CategoryEditorCommitsTargetDraftWhenSavedWithoutBlurAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var category = plan.Categories[0] with
        {
            Target = new(TargetKind.Refill, TargetCadence.Monthly, 100, plan.Today, null, 0, DayOfWeek.Friday),
        };
        var commands = new List<PlanCommand>();
        var component = context.Render<CategoryEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Category, category).Add(item => item.Month, BudgetFacts.Month(plan.Today))
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "275" });
        await SaveButton(component, "Save category").ClickAsync();

        commands.Single().ShouldBeOfType<SaveCategory>().Category.Target.ShouldNotBeNull().Amount.ShouldBe(275m);
    }

    [Fact]
    public async Task InvalidAmountDraftKeepsTransactionSaveBlockedAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<TransactionEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Payees, []).Add(item => item.OnCommand, commands.Add)
            .Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "1 / 0" });

        component.FindAll("[role='alert']").Any(item => item.TextContent.Contains("Enter an amount", StringComparison.Ordinal)).ShouldBeTrue();
        SaveButton(component, "Save transaction").HasAttribute("disabled").ShouldBeTrue();
        commands.ShouldBeEmpty();
    }

    [Fact]
    public async Task MoveMoneySaveRemainsDisabledWhileCommandIsPendingAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = context.Render<MoveMoneyEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Month, BudgetFacts.Month(plan.Today))
            .Add(item => item.OnCommand, async command =>
            {
                commands.Add(command);
                await completion.Task;
            }).Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "25" });
        var save = SaveButton(component, "Move money").ClickAsync();

        await component.WaitForAssertionAsync(() =>
        {
            commands.Count.ShouldBe(1);
            SaveButton(component, "Move money").HasAttribute("disabled").ShouldBeTrue();
        });

        completion.SetResult();
        await save;
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
        return new(Guid.NewGuid(), "Household", "", today, 7,
            [new(Guid.NewGuid(), "Checking", AccountKind.Checking, 0, today, false, "")], [group],
            [new(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null)], [], [], false, false, [])
        { Today = today };
    }

    private static IElement SaveButton<TComponent>(IRenderedComponent<TComponent> component, string label)
        where TComponent : IComponent =>
        component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), label, StringComparison.Ordinal));
}
