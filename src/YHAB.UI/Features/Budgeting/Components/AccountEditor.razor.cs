using System.Globalization;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class AccountEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public AccountData? Account { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter] public bool Busy { get; set; }
    private static readonly AccountKind[] _kinds = Enum.GetValues<AccountKind>();
    private Guid _id;
    private string _name = string.Empty;
    private AccountKind _kind;
    private DateOnly _openedOn;
    private decimal _balance;
    private string _notes = string.Empty;
    private bool _closed;
    private decimal _interest;
    private decimal _minimum;
    private bool _amountValid = true;
    private bool _interestValid = true;
    private bool _minimumValid = true;
    private bool _saving;
    private AmountInput? _balanceInput;
    private AmountInput? _interestInput;
    private AmountInput? _minimumInput;
    private EditorSurface? _surface;
    private string _initial = "";
    private bool HasUnsavedChanges() => !string.Equals(Signature(), _initial, StringComparison.Ordinal)
        || _balanceInput?.HasDraft is true
        || _interestInput?.HasDraft is true
        || _minimumInput?.HasDraft is true;

    protected override void OnInitialized()
    {
        _id = Account?.Id ?? Guid.Empty;
        _name = Account?.Name ?? string.Empty;
        _kind = Account?.Kind ?? AccountKind.Checking;
        _openedOn = Account?.OpenedOn ?? Plan.Today;
        _balance = Account?.OpeningBalance ?? 0;
        _notes = Account?.Notes ?? string.Empty;
        _closed = Account?.Closed ?? false;
        _interest = Account?.InterestRate ?? 0;
        _minimum = Account?.MinimumPayment ?? 0;
        _initial = Signature();
    }

    private string Signature() => DraftSignature.Of(
        _name,
        _kind,
        _openedOn.ToString("O", CultureInfo.InvariantCulture),
        _balance,
        _notes,
        _closed,
        _interest,
        _minimum);

    private async Task SaveAsync()
    {
        if (_saving)
        {
            return;
        }

        _saving = true;
        try
        {
            var balance = await _balanceInput!.CommitAsync();
            if (balance is null)
            {
                return;
            }

            _balance = balance.Value;
            if (_interestInput is not null)
            {
                var interest = await _interestInput.CommitAsync();
                if (interest is null)
                {
                    return;
                }

                _interest = interest.Value;
            }

            if (_minimumInput is not null)
            {
                var minimum = await _minimumInput.CommitAsync();
                if (minimum is null)
                {
                    return;
                }

                _minimum = minimum.Value;
            }

            await OnCommand.InvokeAsync(new SaveAccount(Plan.Version,
                new(_id, _name, _kind, _balance, _openedOn, _closed, _notes, _interest, _minimum)));
        }
        finally
        {
            _saving = false;
        }
    }
    // Cancel is a close action like the heading button, so it shares the draft guard.
    private Task CancelAsync() => _surface is null ? OnClose.InvokeAsync() : _surface.RequestCloseAsync();
}
