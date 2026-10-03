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
    }

    private Task SaveAsync() => OnCommand.InvokeAsync(new SaveAccount(Plan.Version,
        new(_id, _name, _kind, _balance, _openedOn, _closed, _notes, _interest, _minimum)));
    private Task CancelAsync() => OnClose.InvokeAsync();
}
