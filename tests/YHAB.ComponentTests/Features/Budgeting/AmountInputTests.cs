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
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ClearingAnAmountCommitsZeroAndClearsValidationAsync(string text)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var values = new List<decimal>();
        var validity = new List<bool>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 100m).Add(item => item.ValueChanged, values.Add).Add(item => item.ValidChanged, validity.Add));

        await component.Find("fluent-text-input").ChangeAsync(new() { Value = "1 / 0" });
        await component.Find("fluent-text-input").ChangeAsync(new() { Value = text });
        values.ShouldBe([0m]);
        validity.ShouldBe([false, true]);
        component.Render(parameters => parameters.Add(item => item.Value, 0m));
        component.Find("fluent-text-input").GetAttribute("value").ShouldBe("0.00");
        component.FindAll("[role='alert']").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("5 - 5")]
    public async Task UnchangedAmountsDoNotCreateAnotherSaveAsync(string text)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var values = new List<decimal>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 0m).Add(item => item.ValueChanged, values.Add));
        await component.Find("fluent-text-input").ChangeAsync(new() { Value = text });
        values.ShouldBeEmpty();
        component.Find("fluent-text-input").GetAttribute("value").ShouldBe("0.00");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingSaveKeepsDraftThenDisplaysTheParentResultAsync(bool accepted)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 100m).Add(item => item.ValueChanged, _ => completion.Task));

        var change = component.Find("fluent-text-input").ChangeAsync(new() { Value = "200 + 50" });
        component.Render(parameters => parameters.Add(item => item.ReadOnly, true));
        component.Find("fluent-text-input").GetAttribute("value").ShouldBe("250.00");
        component.Render(parameters => parameters.Add(item => item.Value, accepted ? 250m : 100m).Add(item => item.ReadOnly, false));
        completion.SetResult();
        await change;
        await component.WaitForAssertionAsync(() => component.Find("fluent-text-input").GetAttribute("value").ShouldBe(accepted ? "250.00" : "100.00"));
    }

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
