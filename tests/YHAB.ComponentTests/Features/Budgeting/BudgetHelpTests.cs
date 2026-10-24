using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BudgetHelpTests
{
    [Fact]
    public async Task EverySummaryTermOffersAnAccessibleDefinitionAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var board = RenderBoard(context, plan, Budget(plan));

        await board.WaitForAssertionAsync(() => board.FindAll(".budget-term").Count.ShouldBe(5));
        foreach (var term in board.FindAll(".budget-term"))
        {
            var label = term.QuerySelector(".budget-term-label").ShouldNotBeNull().TextContent.Trim();
            label.ShouldNotBeNullOrWhiteSpace();
            var info = term.QuerySelector(".term-info").ShouldNotBeNull();
            info.GetAttribute("aria-label").ShouldBe($"What does {label} mean?");
            info.GetAttribute("title").ShouldNotBeNullOrWhiteSpace();
            info.GetAttribute("aria-expanded").ShouldBe("false");
            var definition = term.QuerySelector(".term-definition").ShouldNotBeNull();
            info.GetAttribute("aria-controls").ShouldBe(definition.Id);
            definition.HasAttribute("hidden").ShouldBeTrue();
        }
    }

    [Fact]
    public async Task SummaryTermDefinitionRevealsCopyAndLinksToTheHelpPanelAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var board = RenderBoard(context, plan, Budget(plan));

        var ready = board.FindAll(".budget-term")[0];
        ready.QuerySelector(".budget-term-label").ShouldNotBeNull().TextContent.Trim().ShouldBe("Ready to assign");
        var info = ready.QuerySelector(".term-info").ShouldNotBeNull();
        await info.ClickAsync();

        board.FindAll(".budget-term")[0].QuerySelector(".term-info").ShouldNotBeNull().GetAttribute("aria-expanded").ShouldBe("true");
        var definition = board.Find("#budget-term-ReadyToAssign");
        definition.HasAttribute("hidden").ShouldBeFalse();
        definition.TextContent.ShouldContain("Money you have that is not assigned to a category yet.");
        board.FindAll(".budget-help").ShouldBeEmpty();

        await definition.QuerySelector(".term-link").ShouldNotBeNull().ClickAsync();

        var help = board.Find(".budget-help");
        help.QuerySelector("h2").ShouldNotBeNull().TextContent.Trim().ShouldBe("How YHAB works");
        help.TextContent.ShouldContain("Money you have that is not assigned to a category yet.");
        board.Find("#budget-term-ReadyToAssign").HasAttribute("hidden").ShouldBeTrue();
    }

    [Fact]
    public async Task StillNeededReplacesUnderfundedAndSplitsTargetsFromOverspendingAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan(withSecondCategory: true);
        var month = BudgetFacts.Month(plan.Today);
        var target = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 150, month, null) };
        plan = plan with { Categories = [target, plan.Categories[1]] };
        var budget = new BudgetMonth(month, 500, 0, 0, 0, 0, 0, 0,
        [
            new(target, 0, 0, 50, 0, 0, 150, 150, false),
            new(plan.Categories[1], 0, 0, -50, 50, 0, 0, 0, false),
        ]);

        var board = RenderBoard(context, plan, budget);

        board.Markup.ShouldNotContain("Underfunded");
        var summary = board.FindAll(".budget-total").Select(item => item.TextContent).ToArray();
        summary.ShouldContain(item => item.Contains("Still needed", StringComparison.Ordinal));
        board.Find(".still-needed-breakdown").TextContent
            .ShouldBe("$150.00 for targets, $50.00 to cover overspending");
        board.Find("fluent-option[text='Still needed']").ShouldNotBeNull();
        board.FindAll("fluent-option[text='Underfunded']").ShouldBeEmpty();
    }

    [Fact]
    public async Task StillNeededAttributesOverspendingBeforeTargetNeedInTheSameCategoryAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var month = BudgetFacts.Month(plan.Today);
        // A $100 monthly balance target with -$50 available needs $150: $50 restores the
        // overspending and $100 completes the target.
        var category = plan.Categories[0] with { Target = new(TargetKind.Balance, TargetCadence.Monthly, 100, month, null) };
        plan = plan with { Categories = [category] };
        var budget = new BudgetMonth(month, 500, 0, 0, -50, 50, 0, 0,
        [
            new(category, 0, 0, -50, 50, 0, 150, 100, false),
        ]);

        var board = RenderBoard(context, plan, budget);

        var stillNeeded = board.FindAll(".budget-total")
            .Single(item => item.TextContent.Contains("Still needed", StringComparison.Ordinal));
        stillNeeded.TextContent.ShouldContain("$150.00");
        board.Find(".still-needed-breakdown").TextContent
            .ShouldBe("$100.00 for targets, $50.00 to cover overspending");
    }

    [Fact]
    public async Task HelpPanelDefinesEveryBudgetTermAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var board = RenderBoard(context, plan, Budget(plan));

        await board.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "How this works", StringComparison.Ordinal)).ClickAsync();

        var terms = board.FindAll(".budget-glossary dt").Select(item => item.TextContent.Trim()).ToArray();
        var definitions = board.FindAll(".budget-glossary dd").Select(item => item.TextContent.Trim()).ToArray();
        terms.ShouldBe(BudgetGlossary.All.Select(BudgetGlossary.Label));
        definitions.ShouldBe(BudgetGlossary.All.Select(BudgetGlossary.Definition));
        definitions.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item));
    }

    [Fact]
    public async Task EditorDescribesTargetBehaviorAndSnoozeAsync()
    {
        await using var context = CreateContext();
        var plan = CreatePlan();
        var month = BudgetFacts.Month(plan.Today);
        var category = plan.Categories[0] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 100, month, null) };

        var editor = context.Render<CategoryEditor>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.Month, month).Add(item => item.Category, category)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnClose, () => { }));

        editor.Find("#target-kind-description").TextContent
            .ShouldBe("Set aside new money each period, even when money is left over.");
        editor.Find("fluent-dropdown").GetAttribute("aria-describedby").ShouldBe("target-kind-description");
        editor.Find("#target-snooze-hint").TextContent
            .ShouldBe("Pause a category's target for this month without deleting it.");
    }

    private static IRenderedComponent<BudgetBoard> RenderBoard(BunitContext context, PlanSnapshot plan, BudgetMonth budget)
        => context.Render<BudgetBoard>(parameters => parameters.Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, _ => { })
            .Add(item => item.LoadMonth, (_, _) => Task.FromResult(budget)));

    private static BudgetMonth Budget(PlanSnapshot plan)
        => new(BudgetFacts.Month(plan.Today), 500, 100, -20, 80, 0, 0, 0, []);

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        return context;
    }

    private static PlanSnapshot CreatePlan(bool withSecondCategory = false)
    {
        var today = new DateOnly(2026, 10, 2);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        var categories = new List<CategoryData> { new(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null) };
        if (withSecondCategory) { categories.Add(new(Guid.NewGuid(), group.Id, "Fun", "", 1, false, null, null)); }

        return new(Guid.NewGuid(), "Help plan", "", today, 7,
            [new(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "")], [group],
            categories, [], [], false, false, [])
        { Today = today };
    }
}
