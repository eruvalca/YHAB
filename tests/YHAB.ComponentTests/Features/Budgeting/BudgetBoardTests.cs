using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BudgetBoardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TargetSnoozeUsesDisplayedMonthWithoutAllocationHistoryAsync(bool snoozed)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var plan = CreatePlan();
        var month = BudgetFacts.Month(plan.Today);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 300, month, null, 0, DayOfWeek.Friday) };
        plan = plan with { Categories = [category] };
        var row = new CategoryMonth(category, 125, -20, 105, 0, 0, snoozed ? 0 : 175, 300, snoozed);
        var budget = new BudgetMonth(month, 875, 125, -20, 105, 0, 0, 0, [row]);
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.LoadMonth, (_, _) => Task.FromResult(budget)).Add(item => item.OnCommand, commands.Add));
        await component.Find(".category-link").ClickAsync();
        var label = snoozed ? "Resume target this month" : "Snooze target this month";
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), label, StringComparison.Ordinal)).ClickAsync();
        var command = commands.Single().ShouldBeOfType<AssignMoney>();
        command.CategoryId.ShouldBe(category.Id);
        command.Month.ShouldBe(month);
        command.Amount.ShouldBe(125);
        command.Snoozed.ShouldBe(!snoozed);
        command.Version.ShouldBe(plan.Version);
        plan.Allocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task NextMonthAssignmentUsesTheDisplayedMonthAndEvaluatedAmountAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var plan = CreatePlan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        await component.Find("fluent-button[aria-label='Next month']").ClickAsync();
        component.Markup.ShouldContain("November 2026");
        await component.Find("fluent-text-input").ChangeAsync(new() { Value = "200 + 50" });
        var assignment = commands.Single().ShouldBeOfType<AssignMoney>();
        assignment.Month.ShouldBe(new(2026, 11, 1));
        assignment.Amount.ShouldBe(250);
        assignment.CategoryId.ShouldBe(plan.Categories[0].Id);
        assignment.Version.ShouldBe(7);
    }

    [Fact]
    public async Task InvalidOpeningBalanceDisablesSavingInsteadOfSubmittingTheOldValueAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var commands = new List<PlanCommand>();
        var component = context.Render<AccountEditor>(parameters => parameters.Add(item => item.Plan, CreatePlan())
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnClose, () => { }));
        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "Checking" });
        await component.FindAll("fluent-text-input")[1].ChangeAsync(new() { Value = "100 / 0" });
        component.Find("[role='alert']").TextContent.ShouldContain("Enter an amount");
        component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save account", StringComparison.Ordinal)).HasAttribute("disabled").ShouldBeTrue();
        commands.ShouldBeEmpty();
    }

    private static PlanSnapshot CreatePlan()
    {
        var today = new DateOnly(2026, 10, 2);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        return new(Guid.NewGuid(), "Private plan", "", today, 7,
            [new(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "")], [group],
            [new(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null)], [], [], false, false, [])
        { Today = today };
    }
}
