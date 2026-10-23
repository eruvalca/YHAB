using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
    public async Task AmountRequestsTheNumericKeyboardOnMobileAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Amount")
            .Add(item => item.Size, TextInputSize.Large));

        // Phones pick their virtual keyboard from `inputmode`; the shared UI
        // initializer mirrors it onto Fluent's shadow control.
        component.Find("fluent-text-input").GetAttribute("inputmode").ShouldBe("decimal");
        component.Find("fluent-text-input").GetAttribute("control-size").ShouldBe("large");
    }

    [Fact]
    public async Task CommaDecimalsFromALocalKeypadAreNotReadAsGroupingAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var values = new List<decimal>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Amount")
            .Add(item => item.Value, 0m).Add(item => item.ValueChanged, values.Add));

        await component.Find("fluent-text-input").ChangeAsync(new() { Value = "12,50" });
        values.ShouldBe([12.50m]);
        component.Render(parameters => parameters.Add(item => item.Value, 12.50m));
        component.Find("fluent-text-input").GetAttribute("value").ShouldBe("12.50");
    }

    [Fact]
    public async Task SignedFieldsOfferASignToggleThatNegatesTheDraftAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var values = new List<decimal>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Opening balance")
            .Add(item => item.Value, 800m).Add(item => item.ValueChanged, values.Add).Add(item => item.AllowNegative, true));

        var sign = component.Find("fluent-button.amount-sign");
        sign.TextContent.Trim().ShouldBe("±");
        sign.GetAttribute("aria-label").ShouldBe("Change the sign of Opening balance");

        await sign.ClickAsync();
        values.ShouldBe([-800m]);
        component.Render(parameters => parameters.Add(item => item.Value, -800m).Add(item => item.AllowNegative, true));
        await component.Find("fluent-button.amount-sign").ClickAsync();
        values.ShouldBe([-800m, 800m]);
        component.Render(parameters => parameters.Add(item => item.Value, 800m).Add(item => item.AllowNegative, true));
        component.Find("fluent-text-input").GetAttribute("value").ShouldBe("800.00");
    }

    [Fact]
    public async Task DirectSignTapNegatesThePendingBlurCommitAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new List<decimal>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 0m).Add(item => item.AllowNegative, true)
            .Add(item => item.ValueChanged, value => { values.Add(value); return completion.Task; }));

        var sign = component.Find("fluent-button.amount-sign");
        await sign.TriggerEventAsync("onpointerdown", new PointerEventArgs());
        var change = component.Find("fluent-text-input").ChangeAsync(new() { Value = "125" });
        await sign.ClickAsync();
        await component.WaitForAssertionAsync(() => values.ShouldBe([-125m]));
        completion.SetResult();
        await change;
        values.ShouldBe([-125m]);
    }

    [Fact]
    public async Task CompletedSaveDoesNotLetHeldSignClickToggleBackAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new List<decimal>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 0m).Add(item => item.AllowNegative, true)
            .Add(item => item.ValueChanged, value => { values.Add(value); return completion.Task; }));

        await component.Find("fluent-button.amount-sign").TriggerEventAsync("onpointerdown", new PointerEventArgs());
        var change = component.Find("fluent-text-input").ChangeAsync(new() { Value = "125" });
        await component.WaitForAssertionAsync(() => values.ShouldBe([-125m]));

        // The parent marks the row busy while the save runs, then completes the
        // save and re-enables the button before the finger is released.
        component.Render(parameters => parameters.Add(item => item.ReadOnly, true));
        completion.SetResult();
        await change;
        component.Render(parameters => parameters.Add(item => item.Value, -125m).Add(item => item.ReadOnly, false));

        var sign = component.Find("fluent-button.amount-sign");
        await sign.TriggerEventAsync("onpointerup", new PointerEventArgs());
        await sign.ClickAsync();
        values.ShouldBe([-125m]);
    }

    [Fact]
    public async Task CancelledSignGestureDoesNotNegateTheNextCommitAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var values = new List<decimal>();
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 0m).Add(item => item.AllowNegative, true)
            .Add(item => item.ValueChanged, values.Add));

        var sign = component.Find("fluent-button.amount-sign");
        await sign.TriggerEventAsync("onpointerdown", new PointerEventArgs());
        await sign.TriggerEventAsync("onpointercancel", new PointerEventArgs());
        await component.Find("fluent-text-input").ChangeAsync(new() { Value = "125" });

        values.ShouldBe([125m]);
    }

    [Fact]
    public async Task FieldsWithoutNegativeAmountsDoNotOfferASignToggleAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Amount")
            .Add(item => item.Value, 0m));

        component.FindAll("fluent-button.amount-sign").ShouldBeEmpty();
    }

    [Fact]
    public async Task TextModeFieldsKeepTheFullKeyboardForSignedEntryAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var component = context.Render<AmountInput>(parameters => parameters.Add(item => item.Label, "Assigned")
            .Add(item => item.Value, 0m).Add(item => item.InputMode, TextInputMode.Text));

        component.Find("fluent-text-input").GetAttribute("inputmode").ShouldBe("text");
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
