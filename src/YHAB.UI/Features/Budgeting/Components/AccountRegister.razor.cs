using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class AccountRegister
{
    private const int PageSize = 50;
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public Guid? AccountId { get; set; }
    [Parameter] public Guid? InitialCategoryId { get; set; }
    [Parameter] public DateOnly? InitialFrom { get; set; }
    [Parameter] public DateOnly? InitialThrough { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback<AccountData> OnEditAccount { get; set; }
    [Parameter] public bool Busy { get; set; }
    // A plain refresh keeps an open draft when its edit target still exists; a
    // command closes it because the command consumed the draft's values.
    [Parameter] public bool ExternalRefresh { get; set; }
    [Parameter] public RenderFragment? PlanActions { get; set; }
    private static readonly string[] _filters = ["All posted", "Recurring"];
    private static readonly string[] _sorts = ["Newest first", "Oldest first", "Payee", "Amount"];
    private readonly HashSet<Guid> _selected = [];
    private readonly Dictionary<Guid, decimal> _running = [];
    private string _search = "";
    private string _filter = "All posted";
    private string _sort = "Newest first";
    private DateOnly? _from;
    private DateOnly? _to;
    private Guid? _categoryId;
    private AccountData? _account;
    private TransactionData? _transaction;
    private bool _editing;
    private bool _deleting;
    private Guid _editKey;
    private long _version = -1;
    private PlanSnapshot? _loadedPlan;
    private Guid? _previousAccount;
    private (Guid?, DateOnly?, DateOnly?)? _initialFilter;
    private int _page;
    private RegisterPage? _result;
    private IReadOnlyList<string>? _payees;
    private readonly List<RegisterCursor?> _cursors = [null];
    private string? _error;
    private int _request;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Borrowed reference: LoadAsync owns and disposes the source after its work completes; component disposal cancels its linked lifetime.")]
    private CancellationTokenSource? _loadCancellation;
    private bool IsBusy { get => Busy || field || _error is not null; set; }
    [Parameter, EditorRequired] public Func<RegisterQuery, CancellationToken, Task<RegisterPage>> LoadPage { get; set; } = default!;
    [Parameter, EditorRequired] public Func<CancellationToken, Task<IReadOnlyList<string>>> LoadPayees { get; set; } = default!;
    [Parameter] public IReadOnlyList<AccountBalance>? Balances { get; set; }
    private IEnumerable<TransactionData> VisibleEntries => _result?.Rows.Select(item => item.Transaction) ?? [];
    private bool AllVisibleSelected => VisibleEntries.Any() && VisibleEntries.All(item => _selected.Contains(item.Id));
    private AccountBalance? SelectedBalance => _account is null ? null : Balances?.Single(item => item.AccountId == _account.Id) ?? BudgetFacts.Balance(Plan, _account, Plan.Today);

    protected override async Task OnParametersSetAsync()
    {
        _account = Plan.Accounts.SingleOrDefault(item => item.Id == AccountId);
        var initialFilter = (InitialCategoryId, InitialFrom, InitialThrough);
        var filterChanged = _initialFilter != initialFilter;
        if (filterChanged)
        {
            _initialFilter = initialFilter;
            _from = InitialFrom;
            _to = InitialThrough;
            _categoryId = InitialCategoryId;
        }
        if (_loadedPlan?.Id != Plan.Id || _version != Plan.Version || _previousAccount != AccountId || filterChanged || (_error is not null && !ReferenceEquals(_loadedPlan, Plan)))
        {
            var planReplaced = _loadedPlan?.Id != Plan.Id;
            var catalogChanged = _version != Plan.Version;
            if (planReplaced || _previousAccount != AccountId || filterChanged || (catalogChanged && !KeepOpenDraft()) || (_error is not null && !ReferenceEquals(_loadedPlan, Plan)))
            {
                CloseEditors();
            }

            // A changed catalog may no longer contain a category referenced by the old page.
            _result = null;
            _payees = null;
            _version = Plan.Version;
            _previousAccount = AccountId;
            _loadedPlan = Plan;
            await ResetPageAsync();
        }
    }

    // Refresh keeps an open editor only while its edit target survives; the
    // editor rebuilds its catalog from the new plan and keeps the typed values.
    private bool KeepOpenDraft()
    {
        if (!ExternalRefresh || !_editing)
        {
            return false;
        }

        if (_transaction is null)
        {
            return true;
        }

        var refreshed = Plan.Transactions.SingleOrDefault(item => item.Id == _transaction.Id);
        if (refreshed is null)
        {
            return false;
        }

        _transaction = refreshed;
        return true;
    }

    private async Task LoadAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        var previous = _loadCancellation;
        _loadCancellation = operation;
        var request = ++_request;
        IsBusy = true;
        _error = null;
        try
        {
            if (previous is not null) { await previous.CancelAsync(); }
            token.ThrowIfCancellationRequested();
            var result = await LoadPage(new(AccountId, _search, _filter, _sort, _from, _to, _cursors[_page], PageSize, Plan.Version, _categoryId), token);
            token.ThrowIfCancellationRequested();
            if (request != _request) { return; }
            var payees = _payees ?? await LoadPayees(token);
            token.ThrowIfCancellationRequested();
            if (request != _request) { return; }
            _result = result;
            _payees = payees;
            _running.Clear();
            foreach (var row in result.Rows) { _running[row.Transaction.Id] = row.RunningBalance; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed or superseded read. */ }
        catch (OperationCanceledException) { if (request == _request) { _error = "Loading transactions timed out. Refresh your plan to try again."; } }
        catch (BudgetRequestException exception) { if (!token.IsCancellationRequested && request == _request) { _error = exception.Message; } }
        catch (HttpRequestException) { if (!token.IsCancellationRequested && request == _request) { _error = "Unable to load transactions. Refresh your plan to try again."; } }
        finally
        {
            if (ReferenceEquals(_loadCancellation, operation)) { _loadCancellation = null; }
            if (!IsDisposed && request == _request) { IsBusy = false; }
        }
    }
    private decimal DisplayAmount(TransactionData entry) => AccountId == entry.TransferAccountId && AccountId.HasValue ? -entry.Amount : entry.Amount;
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
    private string CategoryName(Guid? id) => id.HasValue ? Plan.Categories.SingleOrDefault(item => item.Id == id)?.Name ?? "an unavailable category" : "Ready to assign";
    private void Add() { CloseEditors(); _transaction = null; _editKey = Guid.CreateVersion7(); _editing = true; }
    private void Edit(TransactionData entry) { CloseEditors(); _transaction = entry; _editKey = Guid.CreateVersion7(); _editing = true; }
    private void Duplicate(TransactionData entry) => Edit(entry with { Id = Guid.Empty, Date = Plan.Today, Repeat = RepeatFrequency.None, Splits = entry.Splits.Select(item => item with { Id = Guid.CreateVersion7() }).ToArray() });
    private void CloseEditors() { _editing = false; _deleting = false; }
    private Task EditAccountAsync() => OnEditAccount.InvokeAsync(_account!);
    private void Select(Guid id, bool value) { if (value) { _selected.Add(id); } else { _selected.Remove(id); } }
    private void SelectVisible(bool value) { foreach (var entry in VisibleEntries) { Select(entry.Id, value); } }
    private async Task PreviousPageAsync() { _page--; _selected.Clear(); await LoadAsync(); }
    private async Task ResetPageAsync() { _page = 0; _selected.Clear(); _cursors.Clear(); _cursors.Add(null); await LoadAsync(); }
    private async Task ClearReportFiltersAsync() { _categoryId = null; _from = null; _to = null; await ResetPageAsync(); }
    private async Task NextPageAsync() { if (_result?.Next is not { } next) { return; } _page++; if (_cursors.Count <= _page) { _cursors.Add(next); } else { _cursors[_page] = next; } _selected.Clear(); await LoadAsync(); }
    private void RequestDelete() => _deleting = true;
    private void CancelDelete() => _deleting = false;
    private Task DeleteAsync() => OnCommand.InvokeAsync(new DeleteTransactions(Plan.Version, _selected.ToArray()));
}
