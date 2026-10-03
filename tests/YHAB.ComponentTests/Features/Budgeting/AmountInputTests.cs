using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AmountInputTests
{
    [Fact]
    public async Task CalculatorEmitsEvaluatedAmountAndRejectsInvalidExpressionAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var values = new List<decimal>();
        var validity = new List<bool>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 100m).Add(item => item.ValueChanged, values.Add).Add(item => item.ValidChanged, validity.Add));
        await component.Find("fluent-text-input").ChangeAsync(new() { Value = "(50 + 25) * 2" });
        values.ShouldBe([150m]);
        validity.ShouldBe([true]);
        component.Render(parameters => parameters.Add(item => item.Value, 150m));
        await component.Find("fluent-text-input").ChangeAsync(new() { Value = "50 / 0" });
        values.ShouldBe([150m]);
        validity.ShouldBe([true, false]);
        component.Find("[role='alert']").TextContent.ShouldContain("Enter an amount or expression");
    }
}
