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
public sealed class MoveMoneyEditorTests
{
    [Fact]
    public async Task RefreshKeepsARemovedDestinationAndBlocksSavingAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var group = new GroupData(Guid.CreateVersion7(), "Everyday", 0);
        var first = new CategoryData(Guid.CreateVersion7(), group.Id, "Groceries", "", 0, false, null, null);
        var second = new CategoryData(Guid.CreateVersion7(), group.Id, "Fun", "", 1, false, null, null);
        var plan = new PlanSnapshot(Guid.CreateVersion7(), "Plan", "", today, 7, [], [group], [first, second], [], [], false, false, []) { Today = today };
        var component = context.Render<MoveMoneyEditor>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.Month, today)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnClose, () => { }));

        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "10" });
        SaveButton(component).HasAttribute("disabled").ShouldBeFalse();

        // The destination defaults to the first category; a later catalog no longer offers it, and
        // re-targeting it silently would move money somewhere the user never chose.
        component.Render(parameters => parameters.Add(item => item.Plan, plan with { Categories = [second] }));

        component.Find("[role='alert']").TextContent.ShouldBe("That category is no longer available. Choose another category.");
        SaveButton(component).HasAttribute("disabled").ShouldBeTrue();
    }

    private static IElement SaveButton(IRenderedComponent<MoveMoneyEditor> component)
        => component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Move money", StringComparison.Ordinal));
}
