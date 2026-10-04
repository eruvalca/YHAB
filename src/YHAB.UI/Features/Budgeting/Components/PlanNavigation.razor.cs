using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class PlanNavigation
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public IReadOnlyList<AccountBalance>? Balances { get; set; }
    private decimal WorkingBalance(AccountData account) => Balances?.Single(item => item.AccountId == account.Id).Working ?? BudgetFacts.Balance(Plan, account, Plan.Today).Working;
    [Parameter] public EventCallback OnAddAccount { get; set; }
    [Parameter] public bool Busy { get; set; }
    private Task AddAsync() => OnAddAccount.InvokeAsync();
}
