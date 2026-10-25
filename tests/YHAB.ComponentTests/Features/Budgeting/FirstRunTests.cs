using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class FirstRunTests
{
    private const string ModulePath = "./_content/YHAB.UI/first-run.js";

    [Fact]
    public async Task PlanWithoutAccountsShowsTheAccountActionInTheContentAsync()
    {
        await using var context = CreateContext();
        var added = 0;
        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, CreatePlan() with { Groups = [] })
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnAddAccount, () => added++));

        board.Find(".first-run-account").TextContent.ShouldContain("Add your first account");
        board.Find(".empty-state").TextContent.ShouldContain("Start with an account");
        board.Find(".empty-state").TextContent.ShouldNotContain("Add a category group");
        var add = board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Add your first account", StringComparison.Ordinal));
        await board.WaitForAssertionAsync(() => add.HasAttribute("disabled").ShouldBeFalse());
        await add.ClickAsync();

        added.ShouldBe(1);
    }

    [Fact]
    public async Task PlanFrameKeepsTheFirstAccountActionAvailableWithoutOpeningTheDrawerAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var frame = context.Render<PlanFrame>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.OnAddAccount, () => { })
            .Add(item => item.ChildContent, builder =>
            {
                builder.OpenComponent<BudgetBoard>(0);
                builder.AddComponentParameter(1, nameof(BudgetBoard.Plan), plan);
                builder.AddComponentParameter(2, nameof(BudgetBoard.OnCommand), EventCallback<PlanCommand>.Empty);
                builder.CloseComponent();
            }));

        frame.Find(".plan-drawer").HasAttribute("open").ShouldBeFalse();
        frame.Find(".plan-content .first-run-account").ShouldNotBeNull();
        frame.FindAll(".plan-drawer .first-run-account").ShouldBeEmpty();
    }

    [Fact]
    public async Task PlanFrameProvidesCompactLinksToEveryPlanPageAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var frame = context.Render<PlanFrame>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.ChildContent, builder => builder.AddContent(0, "Plan content")));

        var links = frame.FindAll(".plan-page-tabs a");
        links.Count.ShouldBe(4);
        links.Select(item => item.GetAttribute("href")).ShouldBe([
            $"/plans/{plan.Id}",
            $"/plans/{plan.Id}/accounts",
            $"/plans/{plan.Id}/reports",
            $"/plans/{plan.Id}/settings"]);
        links.Select(item => item.TextContent).ShouldBe(["Plan", "Transactions", "Reflect", "Settings"]);
        frame.Find(".plan-drawer .plan-navigation").ShouldNotBeNull();
    }

    [Fact]
    public async Task PlanFrameKeepsTransactionsActiveOnAnAccountRegisterAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true);
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"http://localhost/plans/{plan.Id}/accounts/{plan.Accounts[0].Id}");
        var frame = context.Render<PlanFrame>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.ChildContent, builder => builder.AddContent(0, "Register content")));

        var active = frame.FindAll(".plan-page-tabs a[aria-current='page']");
        active.Count.ShouldBe(1);
        active[0].TextContent.ShouldBe("Transactions");
    }

    [Fact]
    public async Task ChecklistMarksTheAccountStepAndOffersTheCategoryEditorAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true);
        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));

        await board.WaitForAssertionAsync(() => board.Find(".first-run-checklist").ShouldNotBeNull());
        var steps = board.FindAll(".first-run-steps li");
        steps.Count.ShouldBe(3);
        steps[0].GetAttribute("data-complete").ShouldBe("true");
        steps[1].GetAttribute("data-current").ShouldBe("true");
        steps[1].GetAttribute("aria-current").ShouldBe("step");

        await board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Add a category", StringComparison.Ordinal)).ClickAsync();

        board.Find(".row-panel .inline-name-form").ShouldNotBeNull();
    }

    [Fact]
    public async Task ChecklistOpensTheInlineHelpForTheAssignmentStepAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));

        await board.WaitForAssertionAsync(() => board.Find(".first-run-checklist").ShouldNotBeNull());
        var checklist = board.Find(".first-run-checklist");
        checklist.QuerySelectorAll(".first-run-steps li")[2].GetAttribute("data-current").ShouldBe("true");
        board.FindAll(".budget-help").ShouldBeEmpty();

        await checklist.QuerySelectorAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "How this works", StringComparison.Ordinal)).ClickAsync();

        board.Find(".budget-help").TextContent.ShouldContain("Positive balances roll forward");
    }

    [Fact]
    public async Task DismissedChecklistStaysHiddenWhileKeepingTheAccountActionAsync()
    {
        await using var context = CreateContext();
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDismissed", _ => true).SetResult(true);
        var plan = CreatePlan();

        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));

        await board.WaitForAssertionAsync(() => board.FindAll(".first-run-checklist").ShouldBeEmpty());
        board.Find(".first-run-account").ShouldNotBeNull();
    }

    [Fact]
    public async Task DismissingTheChecklistPersistsTheChoiceAsync()
    {
        await using var context = CreateContext();
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDismissed", _ => false).SetResult(false);
        var plan = CreatePlan();

        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));
        await board.WaitForAssertionAsync(() => board.Find(".first-run-checklist").ShouldNotBeNull());

        await board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Dismiss", StringComparison.Ordinal)).ClickAsync();

        board.FindAll(".first-run-checklist").ShouldBeEmpty();
        module.VerifyInvoke("dismiss");
    }

    [Fact]
    public async Task FundTargetsExplainsWhenNothingNeedsMoneyAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));

        var fund = board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Fund targets", StringComparison.Ordinal));
        fund.HasAttribute("disabled").ShouldBeTrue();
        fund.GetAttribute("aria-describedby").ShouldBe("fund-targets-hint");
        board.Find("#fund-targets-hint").TextContent.ShouldBe("Every target is funded, hidden, or snoozed. Nothing to fund this month.");
    }

    [Fact]
    public async Task FundTargetsExplainsWhenNoMoneyIsReadyAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 100, plan.Today, null) };
        plan = plan with { Accounts = [plan.Accounts[0] with { OpeningBalance = 0 }], Categories = [category] };
        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));

        var fund = board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Fund targets", StringComparison.Ordinal));
        fund.HasAttribute("disabled").ShouldBeTrue();
        board.Find("#fund-targets-hint").TextContent.ShouldBe("Add money to Ready to assign before funding targets.");
    }

    [Fact]
    public async Task MoveMoneyNeedsACategoryAsync()
    {
        await using var context = CreateContext();
        var noCategory = CreatePlan(withAccount: true);
        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, noCategory).Add(item => item.OnCommand, _ => { }));

        var move = board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Move money", StringComparison.Ordinal));
        move.HasAttribute("disabled").ShouldBeTrue();
        board.Find("#move-money-hint").TextContent.ShouldBe("Add at least one category before moving money.");

        var oneCategory = CreatePlan(withAccount: true, withCategory: true);
        board.Render(parameters => parameters.Add(item => item.Plan, oneCategory).Add(item => item.OnCommand, _ => { }));

        await board.WaitForAssertionAsync(() =>
        {
            board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Move money", StringComparison.Ordinal)).HasAttribute("disabled").ShouldBeFalse();
            board.FindAll("#move-money-hint").ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task FundTargetsIsEnabledWhenATargetNeedsMoneyAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var month = BudgetFacts.Month(plan.Today);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 100, month, null) };
        plan = plan with { Categories = [category] };
        var row = new CategoryMonth(category, 0, 0, 0, 0, 0, 100, 100, false);
        var budget = new BudgetMonth(month, 500, 0, 0, 0, 0, 0, 0, [row]);

        var board = RenderBoard(context, plan, budget);

        board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Fund targets", StringComparison.Ordinal))
            .HasAttribute("disabled").ShouldBeFalse();
        board.FindAll("#fund-targets-hint").ShouldBeEmpty();
    }

    [Fact]
    public async Task FundTargetsIsEnabledForOverspendingWithoutTargetsAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var month = BudgetFacts.Month(plan.Today);
        var row = new CategoryMonth(plan.Categories[0], 0, 0, -100, 100, 0, 0, 0, false);
        var budget = new BudgetMonth(month, 500, 0, 0, -100, 100, 0, 0, [row]);
        AutoAssign? command = null;

        var board = RenderBoard(context, plan, budget, item => command = item as AutoAssign);

        var fund = board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Fund targets", StringComparison.Ordinal));
        fund.HasAttribute("disabled").ShouldBeFalse();
        board.FindAll("#fund-targets-hint").ShouldBeEmpty();
        await fund.ClickAsync();
        command.ShouldNotBeNull();
    }

    [Fact]
    public async Task FundTargetsExplainsWhenEveryTargetIsAlreadyFundedAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var month = BudgetFacts.Month(plan.Today);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 100, month, null) };
        plan = plan with { Categories = [category] };
        var row = new CategoryMonth(category, 100, 0, 100, 0, 0, 0, 100, false);
        var budget = new BudgetMonth(month, 500, 100, 0, 100, 0, 0, 0, [row]);

        var board = RenderBoard(context, plan, budget);

        board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Fund targets", StringComparison.Ordinal))
            .HasAttribute("disabled").ShouldBeTrue();
        board.Find("#fund-targets-hint").TextContent.ShouldBe("Every target is funded, hidden, or snoozed. Nothing to fund this month.");
    }

    [Theory]
    [InlineData("category-hidden")]
    [InlineData("group-hidden")]
    [InlineData("snoozed")]
    public async Task FundTargetsExplainsWhenNoEligibleTargetRemainsAsync(string state)
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var month = BudgetFacts.Month(plan.Today);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 100, month, null) };
        var snoozed = string.Equals(state, "snoozed", StringComparison.Ordinal);
        if (string.Equals(state, "category-hidden", StringComparison.Ordinal)) { category = category with { Hidden = true }; }
        if (string.Equals(state, "group-hidden", StringComparison.Ordinal)) { plan = plan with { Groups = [plan.Groups[0] with { Hidden = true }] }; }
        plan = plan with { Categories = [category] };
        var row = new CategoryMonth(category, 0, 0, 0, 0, 0, 100, 100, snoozed);
        var budget = new BudgetMonth(month, 500, 0, 0, 0, 0, 0, 0, [row]);

        var board = RenderBoard(context, plan, budget);

        board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Fund targets", StringComparison.Ordinal))
            .HasAttribute("disabled").ShouldBeTrue();
        board.Find("#fund-targets-hint").TextContent.ShouldBe("Every target is funded, hidden, or snoozed. Nothing to fund this month.");
    }

    [Fact]
    public async Task AssignmentCompletionSurvivesMonthNavigationAsync()
    {
        await using var context = CreateContext();
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDismissed", _ => false).SetResult(false);
        module.Setup<bool>("hasAssigned", _ => false).SetResult(false);
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var month = BudgetFacts.Month(plan.Today);
        plan = plan with { Allocations = [new(plan.Categories[0].Id, month, 300)] };

        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));

        await board.WaitForAssertionAsync(() => module.Invocations.ShouldContain(item => string.Equals(item.Identifier, "completeAssignment", StringComparison.Ordinal)));
        board.FindAll(".first-run-panel").ShouldBeEmpty();

        await board.Find("fluent-button[aria-label='Next month']").ClickAsync();

        board.FindAll(".first-run-panel").ShouldBeEmpty();
        board.FindAll(".first-run-checklist").ShouldBeEmpty();
    }

    [Fact]
    public async Task AssignmentCompletionSurvivesReopeningInAnUnassignedMonthAsync()
    {
        await using var context = CreateContext();
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDismissed", _ => false).SetResult(false);
        module.Setup<bool>("hasAssigned", _ => true).SetResult(true);
        var plan = CreatePlan(withAccount: true, withCategory: true);

        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));

        await board.WaitForAssertionAsync(() => module.Invocations.ShouldContain(item => string.Equals(item.Identifier, "hasAssigned", StringComparison.Ordinal)));
        board.FindAll(".first-run-panel").ShouldBeEmpty();
        board.FindAll(".first-run-checklist").ShouldBeEmpty();
    }

    [Fact]
    public async Task AssignmentCompletionIsRecordedWhileTheCategoryEditorIsOpenAsync()
    {
        await using var context = CreateContext();
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDismissed", _ => false).SetResult(false);
        module.Setup<bool>("hasAssigned", _ => false).SetResult(false);
        var plan = CreatePlan(withAccount: true, withCategory: true);
        var month = BudgetFacts.Month(plan.Today);
        var allocation = new AllocationData(plan.Categories[0].Id, month, 300);

        var board = context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan).Add(item => item.OnCommand, _ => { }));
        await board.Find(".category-row .category-link").ClickAsync();
        board.Find(".category-details").ShouldNotBeNull();

        board.Render(parameters => parameters.Add(item => item.Plan, plan with { Version = 8, Allocations = [allocation] })
            .Add(item => item.OnCommand, _ => { }));

        await board.WaitForAssertionAsync(() => module.Invocations.ShouldContain(item => string.Equals(item.Identifier, "completeAssignment", StringComparison.Ordinal)));
        board.Find(".category-details").ShouldNotBeNull();

        await board.Find("fluent-button[aria-label='Next month']").ClickAsync();
        await board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Close", StringComparison.Ordinal)).ClickAsync();

        board.FindAll(".first-run-panel").ShouldBeEmpty();
        board.FindAll(".category-details").ShouldBeEmpty();
    }

    private static IRenderedComponent<BudgetBoard> RenderBoard(BunitContext context, PlanSnapshot plan, BudgetMonth budget, Action<PlanCommand>? onCommand = null)
        => context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, onCommand ?? (_ => { }))
            .Add(item => item.LoadMonth, (_, _) => Task.FromResult(budget)));

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        return context;
    }

    private static PlanSnapshot CreatePlan(bool withAccount = false, bool withCategory = false)
    {
        var today = new DateOnly(2026, 10, 2);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        return new(Guid.NewGuid(), "New plan", "", today, 7,
            withAccount ? [new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "")] : [],
            [group],
            withCategory ? [new CategoryData(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null)] : [],
            [], [], false, false, [])
        { Today = today };
    }
}
