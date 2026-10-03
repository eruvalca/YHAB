using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;
namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class ReconcileEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public AccountData Account { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter] public bool Busy { get; set; }
    private decimal _balance;
    private decimal _cleared;
    private bool _valid = true;
    private bool _adjust;
    protected override void OnInitialized() { _cleared = BudgetFacts.Balance(Plan, Account, Plan.Today).Cleared; _balance = _cleared; }
    private Task SaveAsync() => OnCommand.InvokeAsync(new ReconcileAccount(Plan.Version, Account.Id, Plan.Today, _balance, _adjust));
}

