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
        await component.Find(".category-row .category-link").ClickAsync();
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

    [Fact]
    public async Task ActionsStayReachableWhenTheMonthReadFailsAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var plan = CreatePlan();
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.LoadMonth, (_, _) => Task.FromException<BudgetMonth>(new HttpRequestException("Disconnected")))
            .Add(item => item.OnCommand, _ => { })
            .Add(item => item.PlanActions, builder => builder.AddMarkupContent(0, "<section class=\"workspace-toolbar\"><button type=\"button\" data-refresh>Refresh</button></section>")));

        await component.WaitForAssertionAsync(() => component.FindAll("[data-kind='error']").Count.ShouldBe(1));

        // The recovery action must stay on the page that reports the failure, after a heading.
        component.FindAll("h1").Count.ShouldBe(1);
        component.Find("[data-refresh]").ShouldNotBeNull();
    }

    [Fact]
    public async Task RefreshKeepsTheCategoryEditorWhileACommandResetsItAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var current = CreatePlan();
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, current)
            .Add(item => item.LoadMonth, (month, _) => Task.FromResult(BudgetCalculator.Calculate(current, month, current.Today)))
            .Add(item => item.OnCommand, _ => { }));

        await component.Find(".category-row .category-link").ClickAsync();
        var draft = component.FindComponent<CategoryEditor>().Instance;

        // A refresh delivers the changed record, but the open draft keeps its typed values.
        current = current with { Version = current.Version + 1, Categories = [current.Categories[0] with { Name = "Groceries renamed" }] };
        component.Render(parameters => parameters.Add(item => item.Plan, current).Add(item => item.ExternalRefresh, true));

        component.FindComponent<CategoryEditor>().Instance.ShouldBeSameAs(draft);
        component.FindAll("h1").Count.ShouldBe(1);

        // The editor's own applied save consumes the draft, so its result starts the editor over.
        current = current with { Version = current.Version + 1 };
        component.Render(parameters => parameters.Add(item => item.Plan, current)
            .Add(item => item.ExternalRefresh, false)
            .Add(item => item.AppliedCommand, new SaveCategory(current.Version - 1, current.Categories[0])));

        component.FindComponent<CategoryEditor>().Instance.ShouldNotBeSameAs(draft);
    }

    [Fact]
    public async Task ARefreshedReorderKeepsThePositionWhenATargetSavesAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var commands = new List<PlanCommand>();
        var current = CreatePlan();
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, current)
            .Add(item => item.LoadMonth, (month, _) => Task.FromResult(BudgetCalculator.Calculate(current, month, current.Today)))
            .Add(item => item.OnCommand, commands.Add));

        await component.Find(".category-row .category-link").ClickAsync();

        // A reorder elsewhere moves this category while its draft stays open. The kept mount follows
        // the refreshed position, so its target save cannot write the stale one back.
        current = current with { Version = current.Version + 1, Categories = [current.Categories[0] with { SortOrder = 4 }] };
        component.Render(parameters => parameters.Add(item => item.Plan, current).Add(item => item.ExternalRefresh, true));

        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Save category", StringComparison.Ordinal)).ClickAsync();

        commands.OfType<SaveCategory>().Single().Category.SortOrder.ShouldBe(4);
    }

    [Fact]
    public async Task ARejectedSaveFollowedByAnUndoKeepsTheTargetDraftAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var commands = new List<PlanCommand>();
        var basePlan = CreatePlan();
        var target = new TargetData(TargetKind.Refill, TargetCadence.Monthly, 100, basePlan.Today, null, 0, DayOfWeek.Friday);
        var current = basePlan with { Categories = [basePlan.Categories[0] with { Target = target }] };
        var component = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, current)
            .Add(item => item.LoadMonth, (month, _) => Task.FromResult(BudgetCalculator.Calculate(current, month, current.Today)))
            .Add(item => item.OnCommand, commands.Add));

        await component.Find(".category-row .category-link").ClickAsync();
        var draft = component.FindComponent<CategoryEditor>().Instance;
        await component.Find(".category-details .amount-input fluent-text-input").ChangeAsync(new() { Value = "250" });

        // A rejected save never advances the plan, so a later toolbar Undo is not this editor's
        // command: it must leave the typed target mounted instead of starting the editor over.
        current = current with { Version = current.Version + 1 };
        component.Render(parameters => parameters.Add(item => item.Plan, current)
            .Add(item => item.ExternalRefresh, false)
            .Add(item => item.AppliedCommand, new UndoChange(current.Version)));

        component.FindComponent<CategoryEditor>().Instance.ShouldBeSameAs(draft);
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Save category", StringComparison.Ordinal)).ClickAsync();

        var saved = commands.OfType<SaveCategory>().Single();
        saved.Version.ShouldBe(current.Version);
        saved.Category.Target.ShouldNotBeNull().Amount.ShouldBe(250);
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
