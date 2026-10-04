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
    private PlanSnapshot? _loadedPlan;
    private Guid? _previousAccount;
    private int _page;
    private RegisterPage? _result;
    private IReadOnlyList<string>? _payees;
    private readonly List<RegisterCursor?> _cursors = [null];
    private string? _error;
    private int _request;
    private bool IsBusy { get => Busy || field || _error is not null; set; }
    [Parameter, EditorRequired] public Func<RegisterQuery, Task<RegisterPage>> LoadPage { get; set; } = default!;
    [Parameter, EditorRequired] public Func<Task<IReadOnlyList<string>>> LoadPayees { get; set; } = default!;
    [Parameter] public IReadOnlyList<AccountBalance>? Balances { get; set; }
    private IEnumerable<TransactionData> VisibleEntries => _result?.Rows.Select(item => item.Transaction) ?? [];
    private bool AllVisibleSelected => VisibleEntries.Any() && VisibleEntries.All(item => _selected.Contains(item.Id));
    private AccountBalance? SelectedBalance => _account is null ? null : Balances?.Single(item => item.AccountId == _account.Id) ?? BudgetFacts.Balance(Plan, _account, Plan.Today);

    protected override async Task OnParametersSetAsync()
    {
        _account = Plan.Accounts.SingleOrDefault(item => item.Id == AccountId);
        if (_loadedPlan?.Id != Plan.Id || _version != Plan.Version || _previousAccount != AccountId || (_error is not null && !ReferenceEquals(_loadedPlan, Plan)))
        {
            CloseEditors();
            // A changed catalog may no longer contain a category referenced by the old page.
            _result = null;
            _payees = null;
            _version = Plan.Version;
            _previousAccount = AccountId;
            _loadedPlan = Plan;
            await ResetPageAsync();
        }
    }

    private async Task LoadAsync()
    {
        var request = ++_request;
        IsBusy = true;
        _error = null;
        try
        {
            var result = await LoadPage(new(AccountId, _search, _filter, _sort, _from, _to, _cursors[_page], PageSize, Plan.Version));
            if (request != _request) { return; }
            var payees = _payees ?? await LoadPayees();
            if (request != _request) { return; }
            _result = result;
            _payees = payees;
            _running.Clear();
            foreach (var row in result.Rows) { _running[row.Transaction.Id] = row.RunningBalance; }
        }
        catch (BudgetRequestException exception) { if (request == _request) { _error = exception.Message; } }
        catch (HttpRequestException) { if (request == _request) { _error = "Unable to load transactions. Refresh your plan to try again."; } }
        finally { if (request == _request) { IsBusy = false; } }
    }
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
    private void Add() { CloseEditors(); _transaction = null; _editKey = Guid.CreateVersion7(); _editing = true; }
    private void Edit(TransactionData entry) { CloseEditors(); _transaction = entry; _editKey = Guid.CreateVersion7(); _editing = true; }
    private void Duplicate(TransactionData entry) => Edit(entry with { Id = Guid.Empty, Date = Plan.Today, State = ClearingState.Uncleared, TransferState = ClearingState.Uncleared, Repeat = RepeatFrequency.None, Splits = entry.Splits.Select(item => item with { Id = Guid.CreateVersion7() }).ToArray() });
    private void Reconcile() { CloseEditors(); _reconciling = true; }
    private void CloseEditors() { _editing = false; _reconciling = false; _deleting = false; }
    private Task EditAccountAsync() => OnEditAccount.InvokeAsync(_account!);
    private void Select(Guid id, bool value) { if (value) { _selected.Add(id); } else { _selected.Remove(id); } }
    private void SelectVisible(bool value) { foreach (var entry in VisibleEntries) { Select(entry.Id, value); } }
    private async Task PreviousPageAsync() { _page--; _selected.Clear(); await LoadAsync(); }
    private async Task ResetPageAsync() { _page = 0; _selected.Clear(); _cursors.Clear(); _cursors.Add(null); await LoadAsync(); }
    private async Task NextPageAsync() { if (_result?.Next is not { } next) { return; } _page++; if (_cursors.Count <= _page) { _cursors.Add(next); } else { _cursors[_page] = next; } _selected.Clear(); await LoadAsync(); }
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
