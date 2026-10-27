using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Workspace(IBudgetClient budgets, NavigationManager navigation, IJSRuntime javascript, WorkspaceCache cache)
{
    [Parameter] public Guid PlanId { get; set; }
    [Parameter] public Guid? AccountId { get; set; }
    [SupplyParameterFromQuery(Name = "category")] public Guid? ReportCategoryId { get; set; }
    [SupplyParameterFromQuery(Name = "from")] public DateOnly? ReportFrom { get; set; }
    [SupplyParameterFromQuery(Name = "to")] public DateOnly? ReportThrough { get; set; }
    // Prerendering already read the catalog and month. Carrying only the display
    // data into the interactive renderer keeps the first interactive paint
    // identical instead of flashing an empty loading state; the ledger and
    // allocations stay server-owned and ledger pages are still read on demand.
    [PersistentState] public PlanView? RestoredView { get; set; }
    [PersistentState] public BudgetMonth? RestoredMonth { get; set; }
    private PlanSnapshot? Snapshot { get; set; }
    private PlanView? _view;
    private DateOnly? _month;
    private PreparedMonth? _preparedMonth;
    private WorkspaceOperation _operation = new();
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Borrowed reference: RefreshAsync owns and disposes the source after its work completes; component disposal cancels its linked lifetime.")]
    private CancellationTokenSource? _refreshCancellation;
    private Task? _poll;
    private bool _hasUpdates;
    private bool _busy;
    private bool _externalRefresh;
    private PlanCommand? _applied;
    private long _requestGeneration;
    private bool IsBusy => _busy || !_operation.CanEdit || !RendererInfo.IsInteractive;
    private string? _error;
    private bool _accountEditor;
    private AccountData? _editingAccount;
    private IJSObjectReference? _module;
    private DotNetObjectReference<Workspace>? _reference;
    private bool IsRegister => AccountId.HasValue || new Uri(navigation.Uri).AbsolutePath.TrimEnd('/').EndsWith("/accounts", StringComparison.Ordinal);
    private string ActionStatus()
    {
        if (_error is not null) { return "Action needed"; }
        if (!RendererInfo.IsInteractive) { return "Connecting…"; }
        return _busy && _operation.Phase == WorkspacePhase.Ready ? "Refreshing…" : _operation.Message;
    }

    protected override void OnInitialized() => navigation.LocationChanged += LocationChanged;

    // Budget and all-transactions routes share the same parameter values. Refresh when only the path changes.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "An async event handler must dispatch every unexpected exception to the Blazor renderer.")]
    private async void LocationChanged(object? sender, LocationChangedEventArgs args)
    {
        try
        {
            await InvokeAsync(() => { if (!IsDisposed) { StateHasChanged(); } });
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Snapshot?.Id != PlanId)
        {
            _requestGeneration++;
            _busy = false;
            _operation = new();
            _view = null;
            _month = null;
            _preparedMonth = null;
            CloseAccount();
            Snapshot = null;
            RestoreDisplay();
            await RefreshAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && RendererInfo.IsInteractive)
        {
            var token = LifetimeToken;
            try
            {
                var module = await javascript.InvokeAsync<IJSObjectReference>("import", token, "./_content/YHAB.UI/budget-workspace.js");
                if (IsDisposed) { await module.DisposeAsync(); return; }
                _module = module;
                _reference = DotNetObjectReference.Create(this);
                await _module.InvokeVoidAsync("connect", token, _reference);
                token.ThrowIfCancellationRequested();
                _poll = WatchRevisionAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed during JS initialization. */ }
        }
    }

    private async Task RefreshAsync()
    {
        if (_busy) { return; }
        using var operation = CreateOperation();
        var token = operation.Token;
        var previous = _refreshCancellation;
        _refreshCancellation = operation;
        var planId = PlanId;
        var generation = ++_requestGeneration;
        _busy = true;
        _error = null;
        // A refresh recovers a save that already committed on a different path than it preserves a
        // draft: the draft's values are already stored, so keeping it would let a second save insert
        // a duplicate. Rejected or idle states leave the draft untouched, and a command that a
        // refresh itself posted keeps whatever the draft state was, even while it is retried.
        var preserveDraft = _operation.Phase == WorkspacePhase.Ready || _operation.PreservesDraft;
        var postDue = false;
        try
        {
            if (previous is not null) { await previous.CancelAsync(); }
            token.ThrowIfCancellationRequested();
            var (view, month) = await ReadWorkspaceAsync(planId, !IsRegister, token);
            token.ThrowIfCancellationRequested();
            if (generation != _requestGeneration) { return; }
            ApplyView(view, month, externalRefresh: preserveDraft);
            _operation = _operation.Loaded();
            // Interactive reads can complete after the first render. Post only after
            // the snapshot arrives, and never mutate the plan during static SSR.
            postDue = RendererInfo.IsInteractive && view.HasDueRecurring;
        }
        catch (BudgetRequestException exception) when (generation == _requestGeneration)
        {
            if (exception.Status is 401 or 403 or 404)
            {
                // Do not keep displaying a plan the current user may no longer read.
                cache.Remove(planId);
                Snapshot = null;
                _view = null;
                _preparedMonth = null;
            }
            _error = exception.Message;
        }
        catch (HttpRequestException) when (generation == _requestGeneration)
        {
            _error = "We couldn't reach your plan. Check your connection and refresh.";
        }
        catch (Exception exception) when (generation != _requestGeneration && exception is BudgetRequestException or HttpRequestException)
        {
            // A previous plan's failed read must not replace the current plan's status.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed or superseded read. */ }
        catch (OperationCanceledException) { if (generation == _requestGeneration) { _error = "Loading your plan timed out. Refresh to try again."; } }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, operation)) { _refreshCancellation = null; }
            if (!IsDisposed && generation == _requestGeneration) { _busy = false; }
        }
        if (!token.IsCancellationRequested && postDue && generation == _requestGeneration && Snapshot is { } plan)
        {
            await ExecuteAsync(new PostRecurring(plan.Version, plan.Today), externalRefresh: preserveDraft);
        }
    }

    /// <summary>
    /// A retry repeats the pending command, so it carries the draft intent that command started with;
    /// a new command from the editor is the draft's own save and never preserves it.
    /// </summary>
    private Task ExecuteAsync(PlanCommand command) => ExecuteAsync(command, _operation.PreservesDraft && ReferenceEquals(command, _operation.Pending));

    private async Task ExecuteAsync(PlanCommand command, bool externalRefresh)
    {
        if (_busy || (!_operation.CanEdit && !(_operation.CanRetry && ReferenceEquals(command, _operation.Pending))))
        {
            return;
        }
        _busy = true;
        using var operation = CreateOperation();
        var token = operation.Token;
        var planId = PlanId;
        var generation = ++_requestGeneration;
        _applied = null;
        _operation = WorkspaceOperation.Start(command, externalRefresh);
        _error = null;
        try
        {
            await budgets.ExecuteAsync(planId, command, token);
            token.ThrowIfCancellationRequested();
            if (generation != _requestGeneration) { return; }
            _operation = _operation.Saved();
            // The write committed, so the views treat this command's result as applied even if the
            // follow-up read fails; only that record can consume an editor's draft.
            _applied = command;
            var (view, month) = await ReadWorkspaceAsync(planId, !IsRegister, token);
            token.ThrowIfCancellationRequested();
            if (generation != _requestGeneration) { return; }
            ApplyView(view, month, externalRefresh);
            _operation = _operation.Loaded();
            if (command is SaveAccount)
            {
                CloseAccount();
            }
        }
        catch (BudgetRequestException exception) when (generation == _requestGeneration)
        {
            var saved = _operation.Phase == WorkspacePhase.Refreshing;
            _operation = saved ? _operation.Interrupted() : WorkspaceOperation.Rejected();
            _error = exception.Status == 409 ? "This plan changed in another tab. Refresh it, then try your change again." : exception.Message;
            if (saved) { _error = _operation.Message; }
        }
        catch (HttpRequestException) when (generation == _requestGeneration)
        {
            _operation = _operation.Interrupted();
            _error = _operation.Message;
        }
        catch (Exception exception) when (generation != _requestGeneration && exception is BudgetRequestException or HttpRequestException)
        {
            // The previous plan still owns its operation; the new plan owns the visible state.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed; a committed write remains committed. */ }
        catch (OperationCanceledException)
        {
            // An HTTP timeout can occur after commit. Retain the original command and operation ID.
            if (generation == _requestGeneration)
            {
                _operation = _operation.Interrupted();
                _error = _operation.Message;
            }
        }
        finally
        {
            if (!IsDisposed && generation == _requestGeneration) { _busy = false; }
        }
    }

    private Task RetryAsync() => _operation is { CanRetry: true, Pending: { } command } ? ExecuteAsync(command) : Task.CompletedTask;
    private async Task<BudgetMonth> LoadMonthAsync(DateOnly month, CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancellationToken, RequestAborted);
        var token = operation.Token;
        token.ThrowIfCancellationRequested();
        _month = month;
        return _preparedMonth is { } prepared && prepared.PlanId == PlanId && prepared.Version == Snapshot!.Version && prepared.Budget.Month == month
            ? prepared.Budget
            : await budgets.ReadMonthAsync(PlanId, month, Snapshot!.Version, token);
    }

    private async Task<(PlanView View, BudgetMonth? Month)> ReadWorkspaceAsync(Guid planId, bool includeMonth, CancellationToken cancellationToken)
    {
        var month = _month;
        // Publish matching navigation and month values together. Only read conflicts
        // are retried, at most three times; a committed command is never resent.
        var conflicts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (includeMonth)
                {
                    var result = await budgets.ReadWorkspaceAsync(planId, month, cancellationToken);
                    return (result.View, result.Month);
                }
                return (await budgets.ReadViewAsync(planId, cancellationToken), null);
            }
            catch (BudgetRequestException exception) when (exception.Status == 409 && ++conflicts < 3)
            {
                // Recover a rejected read without resending the saved command.
            }
        }
    }

    private void RestoreDisplay()
    {
        if (RestoredView is { } restored && restored.Catalog.Id == PlanId)
        {
            RestoreDisplay(restored, RestoredMonth);
            RestoredView = null;
            RestoredMonth = null;
            return;
        }

        if (cache.TryGet(PlanId, out var cached, out var cachedMonth))
        {
            RestoreDisplay(cached, cachedMonth);
        }
    }

    private void RestoreDisplay(PlanView view, BudgetMonth? month)
    {
        // The board and the fresh read must agree with the projection being restored.
        _month = month?.Month;
        ApplyView(view, month, externalRefresh: false);
        _operation = _operation.Loaded();
    }

    private void ApplyView(PlanView view, BudgetMonth? month, bool externalRefresh)
    {
        _view = view;
        Snapshot = view.Catalog;
        _preparedMonth = month is null ? null : new(view.Catalog.Id, view.Catalog.Version, month);
        _hasUpdates = false;
        _externalRefresh = externalRefresh;
        // Cache and persist only what the page renders. The ledger and allocations
        // are server-owned and are re-read by interactive ledger pages.
        var display = WorkspaceDisplayState.Trim(view);
        cache.Store(view.Catalog.Id, display, month);
        if (!RendererInfo.IsInteractive)
        {
            // A snapshot too large for the circuit-start message must not be persisted;
            // the renderer paints the skeleton instead of failing to connect.
            var fits = WorkspaceDisplayState.Fits(display, month);
            RestoredView = fits ? display : null;
            RestoredMonth = fits ? month : null;
        }
    }

    private sealed record PreparedMonth(Guid PlanId, long Version, BudgetMonth Budget);
    private async Task<RegisterPage> LoadRegisterAsync(RegisterQuery query, CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancellationToken, RequestAborted);
        return await budgets.ReadRegisterAsync(PlanId, query with { Version = Snapshot!.Version }, operation.Token);
    }

    private async Task<IReadOnlyList<string>> LoadPayeesAsync(CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancellationToken, RequestAborted);
        return await budgets.ReadPayeesAsync(PlanId, operation.Token);
    }

    private async Task WatchRevisionAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_busy || Snapshot is not { } snapshot) { continue; }
                try
                {
                    var revision = await budgets.ReadRevisionAsync(snapshot.Id, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Snapshot?.Id == snapshot.Id && Snapshot.Version < revision)
                    {
                        _hasUpdates = true;
                        await InvokeAsync(StateHasChanged);
                    }
                }
                catch (BudgetRequestException) { /* An explicit refresh presents authentication errors. */ }
                catch (HttpRequestException) { /* Preserve the user's inputs during a temporary disconnection. */ }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { /* A polling timeout is retried on the next tick. */ }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* The workspace was disposed. */ }
    }

    [JSInvokable]
    public Task HistoryShortcutAsync(bool redo) => InvokeAsync(async () =>
    {
        await (redo ? RedoAsync() : UndoAsync());
        if (!IsDisposed) { StateHasChanged(); }
    });
    private Task UndoAsync() => Snapshot is { CanUndo: true } plan ? ExecuteAsync(new UndoChange(plan.Version)) : Task.CompletedTask;
    private Task RedoAsync() => Snapshot is { CanRedo: true } plan ? ExecuteAsync(new RedoChange(plan.Version)) : Task.CompletedTask;
    private void AddAccount() { _editingAccount = null; _accountEditor = true; }
    private void EditAccount(AccountData account) { _editingAccount = account; _accountEditor = true; }
    private void CloseAccount() => _accountEditor = false;

    protected override async ValueTask DisposeCoreAsync()
    {
        navigation.LocationChanged -= LocationChanged;
        try
        {
            if (_poll is not null) { await _poll; }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                if (_module is not null)
                {
                    // Disconnect must run after component cancellation, with an independent deadline.
                    try { await _module.InvokeVoidAsync("disconnect", cleanup.Token); }
                    finally { await _module.DisposeAsync(); }
                }
            }
            catch (JSDisconnectedException)
            {
                // A disconnected server circuit has already released its browser listeners.
            }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested) { /* Disconnect exceeded its deadline. */ }
            finally
            {
                _reference?.Dispose();
                await base.DisposeCoreAsync();
            }
        }
    }
}
