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
public sealed class BudgetCatalogTests
{
    [Fact]
    public async Task CreatesCategoryInsideEmptyGroupAndRetainsDraftUntilSavedAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        await component.Find($"button[aria-label='Add category to Savings']").ClickAsync();
        await component.Find(".inline-name-form fluent-text-input").ChangeAsync(new() { Value = "  Holiday  " });
        await component.FindAll(".inline-name-form fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save", StringComparison.Ordinal)).ClickAsync();
        var saved = commands.Single().ShouldBeOfType<SaveCategory>();
        saved.Category.Id.ShouldBe(Guid.Empty);
        saved.Category.Name.ShouldBe("Holiday");
        saved.Category.GroupId.ShouldBe(plan.Groups[1].Id);
        saved.Category.Target.ShouldBeNull();
        saved.Version.ShouldBe(plan.Version);
        component.Find(".inline-name-form").ShouldNotBeNull();
        component.Render(p => p.Add(item => item.Plan, plan with { Version = 8, Categories = [.. plan.Categories, saved.Category with { Id = Guid.NewGuid() }] }));
        component.FindAll(".inline-name-form").ShouldBeEmpty();
        component.FindAll(".category-row").Count.ShouldBe(3);
    }

    [Fact]
    public async Task CreatesGroupAndRenamesCategoryWithoutReplacingTargetAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Category group", StringComparison.Ordinal)).ClickAsync();
        await component.Find(".inline-name-form fluent-text-input").ChangeAsync(new() { Value = "Future" });
        await component.Find(".inline-name-form").SubmitAsync();
        var group = commands.Single().ShouldBeOfType<SaveGroup>();
        group.Group.Name.ShouldBe("Future");
        group.Group.SortOrder.ShouldBe(2);
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Cancel", StringComparison.Ordinal)).ClickAsync();
        await component.Find("button[aria-label='Rename Groceries']").ClickAsync();
        await component.Find(".inline-name-form fluent-text-input").ChangeAsync(new() { Value = "Food" });
        await component.Find(".inline-name-form").SubmitAsync();
        commands[^1].ShouldBeOfType<SaveCategory>().Category.ShouldBe(plan.Categories[0] with { Name = "Food" });
        component.FindAll(".category-details").ShouldBeEmpty();
    }

    [Fact]
    public async Task RowSelectionOpensTargetWhileAssignmentKeepsItsOwnActionAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        await component.Find(".category-row fluent-text-input").ChangeAsync(new() { Value = "25" });
        component.FindAll(".category-details").ShouldBeEmpty();
        commands.Single().ShouldBeOfType<AssignMoney>().Amount.ShouldBe(25);
        await component.Find(".category-row").ClickAsync();
        component.Find(".category-details h2").TextContent.ShouldBe("Groceries");
        component.Find(".category-details h3").TextContent.ShouldBe("Target");
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save category", StringComparison.Ordinal)).ClickAsync();
        commands[^1].ShouldBeOfType<SaveCategory>().Category.Target.ShouldBe(plan.Categories[0].Target);
    }

    [Fact]
    public async Task AssignmentRefreshPreservesAnOpenTargetDraftAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        await component.Find(".category-row").ClickAsync();
        await component.Find(".category-details .amount-input fluent-text-input").ChangeAsync(new() { Value = "250" });
        component.Render(p => p.Add(item => item.Plan, plan with
        {
            Version = 8,
            Categories = plan.Categories.Select(item => item with { }).ToArray(),
            Allocations = [new(plan.Categories[1].Id, plan.Today, 25, false)],
        }));
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save category", StringComparison.Ordinal)).ClickAsync();
        var saved = commands.Single().ShouldBeOfType<SaveCategory>();
        saved.Version.ShouldBe(8);
        saved.Category.Id.ShouldBe(plan.Categories[0].Id);
        saved.Category.Target.ShouldNotBeNull().Amount.ShouldBe(250);
    }

    [Fact]
    public async Task DragAndKeyboardMovesUseRevisionAndDestinationAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        await component.Find(".category-row .row-move").DragStartAsync(new());
        await component.Find($"tbody[data-group-id='{plan.Groups[1].Id}'] .group-row").DropAsync(new());
        var moved = commands.Single().ShouldBeOfType<ReorderCategory>();
        moved.CategoryId.ShouldBe(plan.Categories[0].Id);
        moved.GroupId.ShouldBe(plan.Groups[1].Id);
        moved.BeforeCategoryId.ShouldBeNull();
        moved.Version.ShouldBe(7);
        await component.Find(".category-row .row-move").ClickAsync();
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Move down", StringComparison.Ordinal)).ClickAsync();
        commands[^1].ShouldBeOfType<ReorderCategory>().BeforeCategoryId.ShouldBeNull();
        await component.Find($"tbody[data-group-id='{plan.Groups[1].Id}'] .group-row .row-move").DragStartAsync(new());
        await component.Find(".group-row").DropAsync(new());
        var group = commands[^1].ShouldBeOfType<ReorderGroup>();
        group.GroupId.ShouldBe(plan.Groups[1].Id);
        group.BeforeGroupId.ShouldBe(plan.Groups[0].Id);
    }

    [Fact]
    public async Task CollapseAndCancelDoNotSaveAndBusyDragIsIgnoredAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        await component.Find("button[aria-label='Collapse Everyday']").ClickAsync();
        component.FindAll(".category-row").ShouldBeEmpty();
        await component.Find("button[aria-label='Expand Everyday']").ClickAsync();
        component.FindAll(".category-row").Count.ShouldBe(2);
        await component.Find("button[aria-label='Rename Everyday']").ClickAsync();
        await component.Find(".inline-name-form fluent-text-input").ChangeAsync(new() { Value = "Changed" });
        await component.Find(".inline-name-form").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        component.FindAll(".inline-name-form").ShouldBeEmpty();
        component.Render(p => p.Add(item => item.Busy, true));
        await component.Find(".category-row .row-move").DragStartAsync(new());
        await component.FindAll(".group-row")[1].DropAsync(new());
        commands.ShouldBeEmpty();
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        return context;
    }

    private static PlanSnapshot Plan()
    {
        var today = new DateOnly(2026, 10, 1);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        var target = new TargetData(TargetKind.Refill, TargetCadence.Monthly, 100, today, null, 0, DayOfWeek.Friday);
        return new(Guid.NewGuid(), "Plan", "", today, 7, [], [group, new(Guid.NewGuid(), "Savings", 1)],
            [new(Guid.NewGuid(), group.Id, "Groceries", "Preserved note", 0, false, null, target), new(Guid.NewGuid(), group.Id, "Utilities", "", 1, false, null, null)], [], [], false, false, [])
        { Today = today };
    }
}
