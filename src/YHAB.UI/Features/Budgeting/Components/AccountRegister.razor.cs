using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class AccountRegister(IJSRuntime javascript)
{
    private const int PageSize = 50;
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public Guid? AccountId { get; set; }
    [Parameter] public Guid? InitialCategoryId { get; set; }
    [Parameter] public DateOnly? InitialFrom { get; set; }
    [Parameter] public DateOnly? InitialThrough { get; set; }
    /// <summary>
    /// The app-icon shortcut asked for a new transaction. It opens the editor once,
    /// on the first interactive render, and reports back so the caller can clear
    /// the intent from the URL.
    /// </summary>
    [Parameter] public bool InitialNewTransaction { get; set; }
    [Parameter] public EventCallback OnNewTransactionConsumed { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback<AccountData> OnEditAccount { get; set; }
    [Parameter] public bool Busy { get; set; }
    /// <summary>
    /// The workspace's account editor is open, and it pins its own actions to the
    /// bottom of a phone screen, so the docked shortcut has to step aside.
    /// </summary>
    [Parameter] public bool AccountEditing { get; set; }
    // A plain refresh keeps an open draft when its edit target still exists; a
    // command closes it because the command consumed the draft's values.
    [Parameter] public bool ExternalRefresh { get; set; }
    [Parameter] public RenderFragment? PlanActions { get; set; }
    private static readonly string[] _filters = ["All posted", "Recurring"];
    private static readonly string[] _sorts = ["Newest first", "Oldest first", "Payee", "Amount"];
    private readonly HashSet<Guid> _selected = [];
    private readonly Dictionary<Guid, decimal> _running = [];
    private const string LayoutModulePath = "./_content/YHAB.UI/Features/Budgeting/Components/AccountRegister.razor.js";
    private string _search = "";
    private string _filter = "All posted";
    private string _sort = "Newest first";
    private DateOnly? _from;
    private DateOnly? _to;
    private Guid? _categoryId;
    private AccountData? _account;
    private TransactionData? _transaction;
    private Guid? _activeRowId;
    private bool _editing;
    private bool _deleting;
    private bool _reloadAfterClose;
    private bool _newTransactionPending;
    private bool _newTransactionHandled;
    private bool _newTransactionNeedsAccount;
    private Guid _editKey;
    private long _version = -1;
    private PlanSnapshot? _loadedPlan;
    private Guid? _previousAccount;
    private (Guid?, DateOnly?, DateOnly?)? _initialFilter;
    private int _page;
    private RegisterPage? _result;
    private PlanSnapshot? _rowPlan;
    private int _displayPage;
    private IReadOnlyList<string>? _payees;
    private readonly List<RegisterCursor?> _cursors = [null];
    private string? _error;
    private int _request;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Borrowed reference: LoadAsync owns and disposes the source after its work completes; component disposal cancels its linked lifetime.")]
    private CancellationTokenSource? _loadCancellation;
    private IJSObjectReference? _layoutModule;
    private bool IsBusy { get => Busy || field || _error is not null; set; }
    [Parameter, EditorRequired] public Func<RegisterQuery, CancellationToken, Task<RegisterPage>> LoadPage { get; set; } = default!;
    [Parameter, EditorRequired] public Func<CancellationToken, Task<IReadOnlyList<string>>> LoadPayees { get; set; } = default!;
    [Parameter] public IReadOnlyList<AccountBalance>? Balances { get; set; }
    private IEnumerable<TransactionData> VisibleEntries => _result?.Rows.Select(item => item.Transaction) ?? [];
    private bool IsNewDraft => _transaction is null || _transaction.Id == Guid.Empty;
    private int EditorColumnCount => _account is null ? 6 : 7;
    // The account editor pins its own actions to the bottom of a phone screen, so the
    // docked shortcut steps aside while it is open.
    private string DockEditingState => _editing || AccountEditing ? "true" : "false";
    private bool AllVisibleSelected => VisibleEntries.Any() && VisibleEntries.All(item => _selected.Contains(item.Id));
    private AccountBalance? SelectedBalance => _account is null ? null : Balances?.Single(item => item.AccountId == _account.Id) ?? BudgetFacts.Balance(Plan, _account, Plan.Today);

    protected override async Task OnParametersSetAsync()
    {
        if (InitialNewTransaction && !_newTransactionHandled) { _newTransactionPending = true; }
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
            // Recovery after a failed read keeps a draft the refresh preserved, just as a catalog
            // change does; only the plan, account, or filter identity discards it.
            var readFailed = _error is not null && !ReferenceEquals(_loadedPlan, Plan);
            if (planReplaced || _previousAccount != AccountId || filterChanged || ((catalogChanged || readFailed) && !KeepOpenDraft()))
            {
                CloseEditors();
            }

            // Keep same-account rows painted while a revision refreshes. Their
            // labels use the catalog they arrived with until replacement is ready.
            if (planReplaced || _previousAccount != AccountId || filterChanged)
            {
                _result = null;
                _rowPlan = null;
            }
            _payees = null;
            _version = Plan.Version;
            _previousAccount = AccountId;
            _loadedPlan = Plan;
            await ResetPageAsync();
        }
    }

    // Prerendering must not open the editor: the interactive renderer starts over
    // and would produce a second one. The intent opens the editor here, once.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (!_newTransactionPending || !RendererInfo.IsInteractive) { return; }
        _newTransactionPending = false;
        _newTransactionHandled = true;
        // An editor with no account to record against would be unusable.
        if (Plan.Accounts.Any(item => !item.Closed)) { Add(); }
        else { _newTransactionNeedsAccount = true; }
        if (OnNewTransactionConsumed.HasDelegate) { await OnNewTransactionConsumed.InvokeAsync(); }
        if (!IsDisposed) { StateHasChanged(); }
    }

    // Refresh keeps an open transaction draft. The catalog deliberately omits the ledger, so the
    // register cannot prove the edited entry still exists; the save reconciles it instead. A
    // duplicated entry keeps no id and therefore stays a new draft either way.
    private bool KeepOpenDraft() => ExternalRefresh && _editing;

    private async Task LoadAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        var previous = _loadCancellation;
        _loadCancellation = operation;
        var request = ++_request;
        var rowPlan = Plan;
        var page = _page;
        IsBusy = true;
        _error = null;
        try
        {
            if (previous is not null) { await previous.CancelAsync(); }
            token.ThrowIfCancellationRequested();
            var result = await LoadPage(new(AccountId, _search, _filter, _sort, _from, _to, _cursors[_page], PageSize, Plan.Version, _categoryId), token);
            token.ThrowIfCancellationRequested();
            if (request != _request) { return; }
            if (_editing && !IsNewDraft && result.Rows.All(item => item.Transaction.Id != _transaction?.Id))
            {
                _reloadAfterClose = true;
            }
            else
            {
                _reloadAfterClose = false;
                _result = result;
                _rowPlan = rowPlan;
                _displayPage = page;
                _running.Clear();
                foreach (var row in result.Rows) { _running[row.Transaction.Id] = row.RunningBalance; }
            }
            // Payee suggestions must not delay painting the register page.
            StateHasChanged();
            var payees = _payees ?? await LoadPayees(token);
            token.ThrowIfCancellationRequested();
            if (request == _request) { _payees = payees; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed or superseded read. */ }
        catch (OperationCanceledException) { if (request == _request) { _error = "Loading transactions timed out. Refresh your plan to try again."; } }
        catch (BudgetRequestException exception)
        {
            if (!token.IsCancellationRequested && request == _request)
            {
                if (exception.Status is 401 or 403 or 404)
                {
                    _result = null;
                    _rowPlan = null;
                    _running.Clear();
                }
                _error = exception.Message;
            }
        }
        catch (HttpRequestException) { if (!token.IsCancellationRequested && request == _request) { _error = "Unable to load transactions. Refresh your plan to try again."; } }
        finally
        {
            if (ReferenceEquals(_loadCancellation, operation)) { _loadCancellation = null; }
            if (!IsDisposed && request == _request) { IsBusy = false; }
        }
    }
    private decimal DisplayAmount(TransactionData entry) => AccountId == entry.TransferAccountId && AccountId.HasValue ? -entry.Amount : entry.Amount;
    private string MobileDate(TransactionData entry) => entry.Date.Year == Plan.Today.Year
        ? entry.Date.ToString("MM/dd", CultureInfo.InvariantCulture)
        : entry.Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
    private string AccountName(Guid id) => (_rowPlan ?? Plan).Accounts.Single(item => item.Id == id).Name;
    private string PayeeLabel(TransactionData entry)
    {
        if (entry.TransferAccountId is { } target) { return $"Transfer: {AccountName(entry.AccountId)} ↔ {AccountName(target)}"; }
        return string.IsNullOrWhiteSpace(entry.Payee) ? "No payee" : entry.Payee;
    }
    private string CategoryLabel(TransactionData entry)
    {
        var plan = _rowPlan ?? Plan;
        if (entry.Splits.Count > 1) { return string.Join(" · ", entry.Splits.Select(item => CategoryName(plan, item.CategoryId)).Distinct(StringComparer.Ordinal)); }
        return entry.Splits.Count == 1 ? CategoryName(plan, entry.Splits[0].CategoryId) : "Transfer / adjustment";
    }
    private string CategoryName(Guid? id) => CategoryName(Plan, id);
    private static string CategoryName(PlanSnapshot plan, Guid? id) => id.HasValue ? plan.Categories.SingleOrDefault(item => item.Id == id)?.Name ?? "an unavailable category" : "Ready to assign";
    private void Add()
    {
        if (_editing || AccountEditing) { return; }
        _transaction = null;
        _activeRowId = null;
        _editKey = Guid.CreateVersion7();
        _editing = true;
    }
    private void Edit(TransactionData entry)
    {
        if (_editing || AccountEditing) { return; }
        _transaction = entry;
        _activeRowId = entry.Id;
        _editKey = Guid.CreateVersion7();
        _editing = true;
    }
    private void Duplicate(TransactionData entry) => Edit(entry with { Id = Guid.Empty, Date = Plan.Today, Repeat = RepeatFrequency.None, Splits = entry.Splits.Select(item => item with { Id = Guid.CreateVersion7() }).ToArray() });
    private void CloseEditors() { _editing = false; _deleting = false; _reloadAfterClose = false; }
    private async Task CloseEditorAsync()
    {
        var reload = _reloadAfterClose;
        CloseEditors();
        if (reload) { await LoadAsync(); }
    }
    private async Task ActivateRowAsync(TransactionData entry)
    {
        if (IsBusy || _editing || AccountEditing) { return; }
        if (_activeRowId == entry.Id || await IsCompactRegisterAsync())
        {
            Edit(entry);
            return;
        }

        _activeRowId = entry.Id;
    }
    private async Task<bool> IsCompactRegisterAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            _layoutModule ??= await javascript.InvokeAsync<IJSObjectReference>("import", token, LayoutModulePath);
            token.ThrowIfCancellationRequested();
            return await _layoutModule.InvokeAsync<bool>("compactRegister", token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (JSDisconnectedException) { return false; }
        catch (JSException) { return false; }
    }
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
    protected override async ValueTask DisposeCoreAsync()
    {
        if (_layoutModule is not null)
        {
            try { await _layoutModule.DisposeAsync(); }
            catch (JSDisconnectedException) { /* The circuit already released its JavaScript references. */ }
        }

        await base.DisposeCoreAsync();
    }
}
