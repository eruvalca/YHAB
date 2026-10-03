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
    private string _text = string.Empty;
    private string? _error;
    private decimal? _lastValue;

    protected override void OnParametersSet()
    {
        if (_lastValue != Value)
        {
            _lastValue = Value;
            _text = Value.ToString("0.00", CultureInfo.InvariantCulture);
            _error = null;
        }
    }

    private async Task EvaluateAsync(string? expression)
    {
        _text = expression ?? string.Empty;
        if (!AmountExpression.TryEvaluate(expression, out var amount))
        {
            _error = "Enter an amount or expression using + − * / and parentheses.";
            await ValidChanged.InvokeAsync(false);
            return;
        }

        _error = null;
        _text = amount.ToString("0.00", CultureInfo.InvariantCulture);
        _lastValue = amount;
        await ValueChanged.InvokeAsync(amount);
        await ValidChanged.InvokeAsync(true);
    }
}
