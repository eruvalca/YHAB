using System.Globalization;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class AmountInput
{
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;
    [Parameter, EditorRequired] public decimal Value { get; set; }
    [Parameter] public EventCallback<decimal> ValueChanged { get; set; }
    [Parameter] public EventCallback<bool> ValidChanged { get; set; }
    [Parameter] public string Id { get; set; } = $"amount-{Guid.NewGuid():N}";
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool ReadOnly { get; set; }
    private string _text = string.Empty;
    private string? _error;
    private decimal? _lastValue;
    private bool _evaluating;

    protected override void OnParametersSet() => SynchronizeValue();

    private void SynchronizeValue()
    {
        if (!_evaluating && _lastValue != Value)
        {
            _lastValue = Value;
            _text = Value.ToString("0.00", CultureInfo.InvariantCulture);
            _error = null;
        }
    }

    private async Task EvaluateAsync(string? expression)
    {
        _text = expression ?? string.Empty;
        // Acknowledge the control's raw text before normalizing it, even if the
        // resulting amount equals Value (for example clearing an existing zero).
        StateHasChanged();
        await Task.Yield();
        var amount = 0m;
        if (!string.IsNullOrWhiteSpace(expression) && !AmountExpression.TryEvaluate(expression, out amount))
        {
            _error = "Enter an amount or expression using + − * / and parentheses.";
            await ValidChanged.InvokeAsync(false);
            return;
        }

        _error = null;
        _text = amount.ToString("0.00", CultureInfo.InvariantCulture);
        _lastValue = amount;
        if (amount != Value)
        {
            _evaluating = true;
            try
            {
                await ValueChanged.InvokeAsync(amount);
            }
            finally
            {
                _evaluating = false;
                // The parent supplies the saved value, including the old value
                // when a write failed. Keep the draft steady while it is pending.
                SynchronizeValue();
            }
        }
        await ValidChanged.InvokeAsync(true);
    }
}
