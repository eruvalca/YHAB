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
    public async Task CategoryGridKeepsTableSemanticsAroundSortableListsAsync()
    {
        await using var context = CreateContext();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, Plan()).Add(item => item.OnCommand, _ => { }));
        component.Find(".budget-table").GetAttribute("role").ShouldBe("table");
        component.FindAll(".budget-header [role='columnheader']").Select(item => item.TextContent).ShouldBe(["Category", "Assigned", "Activity", "Available"]);
        component.FindAll(".budget-group").ShouldAllBe(item => item.GetAttribute("role") == "rowgroup");
        component.FindAll(".group-row").ShouldAllBe(item => item.GetAttribute("role") == "row");
        var list = component.Find(".fluent-sortable-list");
        list.GetAttribute("role").ShouldBe("none");
        list.GetAttribute("data-item-role").ShouldBe("none");
        component.Find(".category-row").GetAttribute("role").ShouldBe("row");
        component.Find(".category-row").Children.Select(item => item.GetAttribute("role")).ShouldBe(["rowheader", "cell", "cell", "cell"]);
        await component.Find(".category-row .row-move").ClickAsync();
        var moveRow = component.Find(".category-move-row");
        moveRow.GetAttribute("role").ShouldBe("row");
        moveRow.Children.Single().GetAttribute("role").ShouldBe("cell");
        moveRow.Children.Single().GetAttribute("aria-colspan").ShouldBe("4");
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
    public async Task AssignmentFieldOffersDecimalEntryAndASignToggleAsync()
    {
        await using var context = CreateContext();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, Plan()).Add(item => item.OnCommand, _ => { }));
        var input = component.Find(".category-row .amount-input fluent-text-input");
        input.GetAttribute("inputmode").ShouldBe("decimal");
        input.GetAttribute("control-size").ShouldBe("large");
        component.Find(".category-row fluent-button.amount-sign").TextContent.Trim().ShouldBe("±");
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
    public async Task CategoryDropBetweenGroupsShowsNewOrderUntilTheSaveSettlesAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        IRenderedComponent<BudgetBoard>? component = null;
        string[][] pending = [];
        component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, async command =>
        {
            commands.Add(command);
            // The table renders the dropped order once the save yields, before the plan is refreshed.
            await Task.Yield();
            pending = [CategoryNames(component!, plan.Groups[0].Id), CategoryNames(component!, plan.Groups[1].Id)];
        }));
        var savings = List(component, plan.Groups[1].Id);
        await component.InvokeAsync(() => savings.OnAddJSAsync(0, 0, ListId(plan.Groups[0].Id), ListId(plan.Groups[1].Id), plan.Categories[0].Id));
        var moved = commands.Single().ShouldBeOfType<ReorderCategory>();
        moved.CategoryId.ShouldBe(plan.Categories[0].Id);
        moved.GroupId.ShouldBe(plan.Groups[1].Id);
        moved.BeforeCategoryId.ShouldBeNull();
        moved.Version.ShouldBe(7);
        pending.ShouldBe([["Utilities"], ["Groceries"]]);
        CategoryNames(component, plan.Groups[0].Id).ShouldBe(["Groceries", "Utilities"]);
        component.FindAll($"[data-group-id='{plan.Groups[1].Id}'] .category-row").ShouldBeEmpty();
    }

    [Fact]
    public async Task ReorderKeepsOpenCategoryDraftWhenThePlanUpdatesAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var group = plan.Groups[0].Id;
        var commands = new List<PlanCommand>();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IRenderedComponent<BudgetBoard>? component = null;
        component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, async command =>
        {
            commands.Add(command);
            if (command is ReorderCategory) { await hold.Task; }
        }));
        await component.Find("button[aria-label='Rename Groceries']").ClickAsync();
        await component.Find(".inline-name-form fluent-text-input").ChangeAsync(new() { Value = "Food" });
        var list = List(component, group);
        // The reorder renders the destination order while the save is held, which recreates the row.
#pragma warning disable CA2025 // The task is awaited in finally before the render context can be disposed.
        var reorder = component.InvokeAsync(() => list.OnUpdateJSAsync(0, 1, ListId(group), ListId(group)));
#pragma warning restore CA2025
        try
        {
            await component.WaitForAssertionAsync(() => CategoryIds(component, group).ShouldBe([plan.Categories[1].Id.ToString(), plan.Categories[0].Id.ToString()]));
            component.FindAll(".inline-name-form").Count.ShouldBe(1);
            component.FindComponents<FluentTextInput>().Single(item => string.Equals(item.Instance.Label, "Category name", StringComparison.Ordinal))
                .Instance.Value.ShouldBe("Food");
        }
        finally
        {
            hold.TrySetResult();
            await reorder;
        }
        component.Render(p => p.Add(item => item.Plan, plan with
        {
            Version = plan.Version + 1,
            Categories = [plan.Categories[0] with { SortOrder = 1 }, plan.Categories[1] with { SortOrder = 0 }],
        }));
        component.FindAll(".inline-name-form").Count.ShouldBe(1);
        await component.Find(".inline-name-form").SubmitAsync();
        var saved = commands[^1].ShouldBeOfType<SaveCategory>();
        saved.Category.Id.ShouldBe(plan.Categories[0].Id);
        saved.Category.Name.ShouldBe("Food");
        saved.Category.SortOrder.ShouldBe(1);
    }

    [Fact]
    public async Task ReorderRejectionKeepsOpenCategoryDraftAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var group = plan.Groups[0].Id;
        var commands = new List<PlanCommand>();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IRenderedComponent<BudgetBoard>? component = null;
        component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, async command =>
        {
            commands.Add(command);
            if (command is ReorderCategory) { await hold.Task; }
        }));
        await component.Find("button[aria-label='Rename Groceries']").ClickAsync();
        await component.Find(".inline-name-form fluent-text-input").ChangeAsync(new() { Value = "Food" });
        var list = List(component, group);
#pragma warning disable CA2025 // The task is awaited in finally before the render context can be disposed.
        var reorder = component.InvokeAsync(() => list.OnUpdateJSAsync(0, 1, ListId(group), ListId(group)));
#pragma warning restore CA2025
        try
        {
            await component.WaitForAssertionAsync(() => CategoryIds(component, group).ShouldBe([plan.Categories[1].Id.ToString(), plan.Categories[0].Id.ToString()]));
        }
        finally
        {
            hold.TrySetResult();
            await reorder;
        }
        // The rejected save restores the saved order without a new plan revision.
        await component.WaitForAssertionAsync(() => CategoryIds(component, group).ShouldBe([plan.Categories[0].Id.ToString(), plan.Categories[1].Id.ToString()]));
        component.FindAll(".inline-name-form").Count.ShouldBe(1);
        await component.Find(".inline-name-form").SubmitAsync();
        commands[^1].ShouldBeOfType<SaveCategory>().Category.ShouldBe(plan.Categories[0] with { Name = "Food" });
    }

    [Fact]
    public async Task CategoryDropWithinFilteredGroupKeepsHiddenNeighborsInPlaceAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var group = plan.Groups[0].Id;
        plan = plan with { Categories = [.. plan.Categories, new(Guid.NewGuid(), group, "Archive", "", 2, true, null, null)] };
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        var list = List(component, group);
        await component.InvokeAsync(() => list.OnUpdateJSAsync(0, 0, ListId(group), ListId(group)));
        commands.ShouldBeEmpty();
        await component.InvokeAsync(() => list.OnUpdateJSAsync(0, 1, ListId(group), ListId(group)));
        var moved = commands.Single().ShouldBeOfType<ReorderCategory>();
        moved.CategoryId.ShouldBe(plan.Categories[0].Id);
        moved.GroupId.ShouldBe(group);
        moved.BeforeCategoryId.ShouldBe(plan.Categories[2].Id);
        await component.InvokeAsync(() => list.OnUpdateJSAsync(1, 0, ListId(group), ListId(group)));
        commands[^1].ShouldBeOfType<ReorderCategory>().BeforeCategoryId.ShouldBe(plan.Categories[0].Id);
    }

    [Fact]
    public async Task MoveControlsAndGroupDragUseRevisionAndDestinationAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var commands = new List<PlanCommand>();
        var component = context.Render<BudgetBoard>(p => p.Add(item => item.Plan, plan).Add(item => item.OnCommand, commands.Add));
        component.FindAll(".category-row .row-move.sortable-grab").Count.ShouldBe(2);
        await component.Find(".category-row .row-move").ClickAsync();
        await component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Move down", StringComparison.Ordinal)).ClickAsync();
        var moved = commands.Single().ShouldBeOfType<ReorderCategory>();
        moved.CategoryId.ShouldBe(plan.Categories[0].Id);
        moved.BeforeCategoryId.ShouldBeNull();
        moved.Version.ShouldBe(7);
        await component.Find($"[data-group-id='{plan.Groups[1].Id}'] .group-row .row-move").DragStartAsync(new());
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
        component.FindAll(".category-row .row-move.sortable-grab").ShouldBeEmpty();
        var list = List(component, plan.Groups[0].Id);
        await component.InvokeAsync(() => list.OnUpdateJSAsync(0, 1, ListId(plan.Groups[0].Id), ListId(plan.Groups[0].Id)));
        commands.ShouldBeEmpty();
    }

    private static string ListId(Guid group) => $"category-list-{group:N}";

    private static FluentSortableList<Guid> List(IRenderedComponent<BudgetBoard> component, Guid group) =>
        component.FindComponents<FluentSortableList<Guid>>().Single(item => string.Equals(item.Instance.Id, ListId(group), StringComparison.Ordinal)).Instance;

    private static string[] CategoryNames(IRenderedComponent<BudgetBoard> component, Guid group) =>
        component.FindAll($"[data-group-id='{group}'] .category-row .category-link").Select(item => item.TextContent.Trim()).ToArray();
    private static string[] CategoryIds(IRenderedComponent<BudgetBoard> component, Guid group) =>
        component.FindAll($"[data-group-id='{group}'] .category-row[data-category-id]").Select(item => item.GetAttribute("data-category-id")!).ToArray();
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
