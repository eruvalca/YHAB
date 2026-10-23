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
public sealed class AvailablePillTests
{
    [Fact]
    public async Task MoveDraftClosesWhenTheAvailableDirectionChangesAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var groceries = plan.Categories[0];
        var commands = new List<PlanCommand>();
        var overspent = Row(groceries, 0m, -25m, -25m);
        var component = context.Render<AvailablePill>(parameters => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.Budget, Budget(plan.Today, 100m, overspent))
            .Add(item => item.Row, overspent)
            .Add(item => item.OnCommand, commands.Add)
            .Add(item => item.Tone, "negative"));

        await component.Find("button.available-pill").ClickAsync();
        component.Find("fluent-popover-b").GetAttribute("opened").ShouldBe("true");

        var funded = Row(groceries, 0m, 25m, 25m);
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.Budget, Budget(plan.Today, 100m, funded))
            .Add(item => item.Row, funded)
            .Add(item => item.OnCommand, commands.Add)
            .Add(item => item.Tone, "positive"));

        component.Find("fluent-popover-b").GetAttribute("opened").ShouldBe("false");
        commands.ShouldBeEmpty();
    }

    [Fact]
    public async Task ChangingTheCoverSourceReplacesAnInvalidDraftAsync()
    {
        await using var context = CreateContext();
        var plan = Plan();
        var groceries = plan.Categories[0];
        var utilities = plan.Categories[1];
        var commands = new List<PlanCommand>();
        var overspent = Row(groceries, 0m, -25m, -25m);
        var funded = Row(utilities, 10m, 0m, 10m);
        var component = context.Render<AvailablePill>(parameters => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.Budget, Budget(plan.Today, 100m, overspent, funded))
            .Add(item => item.Row, overspent)
            .Add(item => item.OnCommand, commands.Add)
            .Add(item => item.Tone, "negative"));

        await component.Find("button.available-pill").ClickAsync();
        await component.Find(".money-popover fluent-text-input").ChangeAsync(new() { Value = "1 / 0" });
        MoveButton(component).HasAttribute("disabled").ShouldBeTrue();

        var dropdown = component.Find(".money-popover fluent-dropdown");
        var option = dropdown.QuerySelector("fluent-option[text='Utilities']").ShouldNotBeNull();
        await dropdown.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = option.Id });

        component.Find(".money-popover fluent-text-input").GetAttribute("value").ShouldBe("10.00");
        component.FindAll(".money-popover [role='alert']").ShouldBeEmpty();
        MoveButton(component).HasAttribute("disabled").ShouldBeFalse();
    }

    private static IElement MoveButton(IRenderedComponent<AvailablePill> component)
        => component.FindAll(".money-popover fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Move money", StringComparison.Ordinal));

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        return context;
    }

    private static BudgetMonth Budget(DateOnly month, decimal readyToAssign, params CategoryMonth[] rows)
        => new(month, readyToAssign, rows.Sum(item => item.Assigned), rows.Sum(item => item.Activity),
            rows.Sum(item => item.Available), 0m, 0m, 0m, rows);

    private static CategoryMonth Row(CategoryData category, decimal assigned, decimal activity, decimal available)
        => new(category, assigned, activity, available, 0m, 0m, 0m, 0m, false);

    private static PlanSnapshot Plan()
    {
        var today = new DateOnly(2026, 10, 1);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        return new(Guid.NewGuid(), "Plan", "", today, 7, [], [group],
            [new(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null),
             new(Guid.NewGuid(), group.Id, "Utilities", "", 1, false, null, null)],
            [], [], false, false, [])
        { Today = today };
    }
}
