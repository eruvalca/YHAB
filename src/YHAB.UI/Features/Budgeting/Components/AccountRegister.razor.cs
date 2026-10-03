using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class AccountRegister
{
    private const int PageSize = 50;
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public Guid? AccountId { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback<AccountData> OnEditAccount { get; set; }
    [Parameter] public bool Busy { get; set; }
    private static readonly string[] _filters = ["All posted", "Needs approval", "Uncleared", "Cleared", "Reconciled", "Recurring"];
    private static readonly string[] _sorts = ["Newest first", "Oldest first", "Payee", "Amount"];
    private readonly HashSet<Guid> _selected = [];
    private readonly Dictionary<Guid, decimal> _running = [];
    private string _search = "";
    private string _filter = "All posted";
    private string _sort = "Newest first";
    private DateOnly? _from;
    private DateOnly? _to;
    private AccountData? _account;
    private TransactionData? _transaction;
    private bool _editing;
    private bool _reconciling;
    private bool _deleting;
    private Guid _editKey;
    private long _version = -1;
    private Guid? _previousAccount;
    private int _page;
    private IEnumerable<TransactionData> AccountEntries => Plan.Transactions.Where(item => !AccountId.HasValue || item.AccountId == AccountId || item.TransferAccountId == AccountId);
    private IEnumerable<TransactionData> FilteredEntries => Sort(AccountEntries.Where(Matches));
    private IEnumerable<TransactionData> VisibleEntries => FilteredEntries.Skip(_page * PageSize).Take(PageSize);
    private bool AllVisibleSelected => VisibleEntries.Any() && VisibleEntries.All(item => _selected.Contains(item.Id));

    protected override void OnParametersSet()
    {
        _account = Plan.Accounts.SingleOrDefault(item => item.Id == AccountId);
        if (_version != Plan.Version || _previousAccount != AccountId)
        {
            CloseEditors();
            _selected.Clear();
            _version = Plan.Version;
            _previousAccount = AccountId;
            _page = 0;
            _running.Clear();
            var balance = _account?.OpeningBalance ?? 0;
            foreach (var entry in AccountEntries.Where(item => item.Repeat == RepeatFrequency.None).OrderBy(item => item.Date).ThenBy(item => item.Id))
            {
                balance += DisplayAmount(entry);
                _running[entry.Id] = balance;
            }
        }
        _page = Math.Min(_page, Math.Max(0, (FilteredEntries.Count() - 1) / PageSize));
    }

    private bool Matches(TransactionData entry)
    {
        if (entry.Date < _from || entry.Date > _to) { return false; }
        var included = _filter switch
        {
            "Recurring" => entry.Repeat != RepeatFrequency.None,
            "Needs approval" => entry.Repeat == RepeatFrequency.None && entry.NeedsApproval,
            "Uncleared" => entry.Repeat == RepeatFrequency.None && DisplayState(entry) == ClearingState.Uncleared,
            "Cleared" => entry.Repeat == RepeatFrequency.None && DisplayState(entry) == ClearingState.Cleared,
            "Reconciled" => entry.Repeat == RepeatFrequency.None && DisplayState(entry) == ClearingState.Reconciled,
            _ => entry.Repeat == RepeatFrequency.None,
        };
        return included && $"{entry.Payee} {entry.Memo} {entry.Flag} {CategoryLabel(entry)} {AccountName(entry.AccountId)} {BudgetFacts.Money(DisplayAmount(entry))}"
            .Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    private IOrderedEnumerable<TransactionData> Sort(IEnumerable<TransactionData> entries) => _sort switch
    {
        "Oldest first" => entries.OrderBy(item => item.Date).ThenBy(item => item.Id),
        "Payee" => entries.OrderBy(item => PayeeLabel(item), StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.Date),
        "Amount" => entries.OrderBy(DisplayAmount).ThenByDescending(item => item.Date),
        _ => entries.OrderByDescending(item => item.Date).ThenByDescending(item => item.Id),
    };
    private decimal DisplayAmount(TransactionData entry) => AccountId == entry.TransferAccountId && AccountId.HasValue ? -entry.Amount : entry.Amount;
    private ClearingState DisplayState(TransactionData entry) => AccountId == entry.TransferAccountId && AccountId.HasValue ? entry.TransferState : entry.State;
    private string AccountName(Guid id) => Plan.Accounts.Single(item => item.Id == id).Name;
    private string PayeeLabel(TransactionData entry)
    {
        if (entry.TransferAccountId is { } target) { return $"Transfer: {AccountName(entry.AccountId)} ↔ {AccountName(target)}"; }
        return string.IsNullOrWhiteSpace(entry.Payee) ? "No payee" : entry.Payee;
    }
    private string CategoryLabel(TransactionData entry)
    {
        if (entry.Splits.Count > 1) { return string.Join(" · ", entry.Splits.Select(item => CategoryName(item.CategoryId)).Distinct(StringComparer.Ordinal)); }
        return entry.Splits.Count == 1 ? CategoryName(entry.Splits[0].CategoryId) : "Transfer / adjustment";
    }
    private string CategoryName(Guid? id) => id.HasValue ? Plan.Categories.Single(item => item.Id == id).Name : "Ready to assign";
    private void Add() { CloseEditors(); _transaction = null; _editKey = Guid.NewGuid(); _editing = true; }
    private void Edit(TransactionData entry) { CloseEditors(); _transaction = entry; _editKey = Guid.NewGuid(); _editing = true; }
    private void Duplicate(TransactionData entry) => Edit(entry with { Id = Guid.Empty, Date = Plan.Today, State = ClearingState.Uncleared, TransferState = ClearingState.Uncleared, Repeat = RepeatFrequency.None, Splits = entry.Splits.Select(item => item with { Id = Guid.NewGuid() }).ToArray() });
    private void Reconcile() { CloseEditors(); _reconciling = true; }
    private void CloseEditors() { _editing = false; _reconciling = false; _deleting = false; }
    private Task EditAccountAsync() => OnEditAccount.InvokeAsync(_account!);
    private void Select(Guid id, bool value) { if (value) { _selected.Add(id); } else { _selected.Remove(id); } }
    private void SelectVisible(bool value) { foreach (var entry in VisibleEntries) { Select(entry.Id, value); } }
    private void PreviousPage() => _page--;
    private void ResetPage() { _page = 0; _selected.Clear(); }
    private void NextPage() => _page++;
    private void RequestDelete() => _deleting = true;
    private void CancelDelete() => _deleting = false;
    private Task DeleteAsync() => OnCommand.InvokeAsync(new DeleteTransactions(Plan.Version, _selected.ToArray()));
    private Task ApproveAsync() => ChangeStatesAsync(null, true);
    private Task ClearAsync() => ChangeStatesAsync(ClearingState.Cleared, false);
    private Task UnclearAsync() => ChangeStatesAsync(ClearingState.Uncleared, false);
    private Task ChangeStatesAsync(ClearingState? state, bool approve) => OnCommand.InvokeAsync(new UpdateTransactionStates(Plan.Version, _selected.ToArray(), state, approve, AccountId));
    private Task ToggleClearedAsync(TransactionData entry) => OnCommand.InvokeAsync(new UpdateTransactionStates(Plan.Version, [entry.Id],
        DisplayState(entry) == ClearingState.Uncleared ? ClearingState.Cleared : ClearingState.Uncleared, false, AccountId));
}
