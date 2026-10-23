using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class AmountInput
{
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;
    [Parameter, EditorRequired] public decimal Value { get; set; }
    [Parameter] public EventCallback<decimal> ValueChanged { get; set; }
    [Parameter] public EventCallback<bool> ValidChanged { get; set; }
    [Parameter] public string Id { get; set; } = $"amount-{Guid.CreateVersion7():N}";
    [Parameter] public TextInputSize Size { get; set; } = TextInputSize.Medium;
    [Parameter] public TextInputMode InputMode { get; set; } = TextInputMode.Decimal;
    [Parameter] public bool AllowNegative { get; set; }
    [Parameter] public bool RejectNegative { get; set; }
    [Parameter] public bool CommitOnSave { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool ReadOnly { get; set; }
    private const string ExpressionError = "Enter an amount or expression using + − * / and parentheses.";
    private const string NonNegativeError = "Enter an amount of zero or more.";
    private string _text = string.Empty;
    private string? _error;
    private decimal? _lastValue;
    private bool _evaluating;
    private bool _negateCommit;
    private bool _pointerGesture;
    private bool _pointerReleased;
    private int _signGesture;
    private int? _suppressedSignGesture;

    protected override void OnParametersSet()
    {
        SynchronizeValue();
        if (_pointerGesture && _pointerReleased && (Disabled || ReadOnly))
        {
            // The released gesture cannot deliver its click while disabled.
            _pointerGesture = false;
            _pointerReleased = false;
            _suppressedSignGesture = null;
        }
    }

    private void SynchronizeValue()
    {
        if (!_evaluating && _lastValue != Value)
        {
            _lastValue = Value;
            _text = Value.ToString("0.00", CultureInfo.InvariantCulture);
            _error = null;
        }
    }

    // The sign button is pressed while the amount field still has focus. Its
    // pointerdown runs before the blur that commits the draft, so let that
    // commit carry the negation instead of racing the now-disabled button.
    private void PrepareSignToggle()
    {
        if (Disabled || ReadOnly || CommitOnSave)
        {
            return;
        }

        _signGesture++;
        _pointerGesture = true;
        _pointerReleased = false;
        _suppressedSignGesture = null;
        _negateCommit = true;
    }

    // A keyboard activation is a new gesture even without pointerdown, so any
    // suppression left by a click that was dropped while disabled is stale.
    private void SignFocusIn()
    {
        if (_pointerGesture)
        {
            return;
        }

        _signGesture++;
        _suppressedSignGesture = null;
        _negateCommit = false;
    }

    private void EndSignPointer()
    {
        _pointerReleased = true;
        // Touch-generated focus can arrive after pointerup. Keep the gesture
        // active through that focus and let the click end it.
        if (Disabled || ReadOnly)
        {
            // The release cannot produce a click while the button is disabled.
            _pointerGesture = false;
            _pointerReleased = false;
            _suppressedSignGesture = null;
        }
    }

    private void CancelSignPointer()
    {
        _pointerGesture = false;
        _pointerReleased = false;
        _negateCommit = false;
        _suppressedSignGesture = null;
    }

    private void FocusIn() => _negateCommit = false;

    private async Task EvaluateAsync(string? expression)
    {
        _text = expression ?? string.Empty;
        var signPending = _negateCommit;
        if (signPending)
        {
            // Consume before yielding so the button's click cannot toggle twice.
            _negateCommit = false;
            _suppressedSignGesture = _signGesture;
        }

        if (CommitOnSave)
        {
            if (TryGetAmount(_text, out var draftAmount, out var draftError))
            {
                _error = null;
                _lastValue = draftAmount;
                if (draftAmount != Value)
                {
                    await ValueChanged.InvokeAsync(draftAmount);
                }

                await ValidChanged.InvokeAsync(true);
            }
            else
            {
                _error = draftError;
                await ValidChanged.InvokeAsync(false);
            }

            return;
        }

        // Acknowledge the control's raw text before normalizing it, even if the
        // resulting amount equals Value (for example clearing an existing zero).
        StateHasChanged();
        await Task.Yield();
        if (!TryGetAmount(_text, out var amount, out var error))
        {
            _error = error;
            await ValidChanged.InvokeAsync(false);
            return;
        }

        if (signPending)
        {
            amount = -amount;
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

    internal async Task<decimal?> CommitAsync()
    {
        if (!TryGetAmount(_text, out var amount, out var error))
        {
            _error = error;
            await ValidChanged.InvokeAsync(false);
            StateHasChanged();
            return null;
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
                SynchronizeValue();
            }
        }

        await ValidChanged.InvokeAsync(true);
        return amount;
    }

    private bool TryGetAmount(string expression, out decimal amount, out string? error)
    {
        amount = 0m;
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        if (!AmountExpression.TryEvaluate(expression, out amount))
        {
            error = ExpressionError;
            return false;
        }

        if (RejectNegative && amount < 0m)
        {
            error = NonNegativeError;
            return false;
        }

        return true;
    }

    private async Task ToggleSignAsync()
    {
        if (Disabled || ReadOnly)
        {
            return;
        }

        _pointerGesture = false;
        _pointerReleased = false;
        if (_suppressedSignGesture == _signGesture)
        {
            _suppressedSignGesture = null;
            return;
        }

        _negateCommit = false;
        // Negate what the field currently shows, falling back to the saved value
        // when the draft is not yet a valid amount. Reusing the evaluation path
        // keeps validation and change notification in one place.
        if (!AmountExpression.TryEvaluate(_text, out var current))
        {
            current = Value;
        }

        await EvaluateAsync((-current).ToString("0.00", CultureInfo.InvariantCulture));
    }
}
