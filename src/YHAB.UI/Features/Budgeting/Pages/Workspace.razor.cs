using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Workspace(IBudgetClient budgets, NavigationManager navigation, IJSRuntime javascript)
{
    [Parameter] public Guid PlanId { get; set; }
    [Parameter] public Guid? AccountId { get; set; }
    // Each renderer loads its owner-scoped catalog; ledger pages and month totals
    // are fetched separately instead of round-tripping a ledger in prerender state.
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
    private long _requestGeneration;
    private bool IsBusy => _busy || !_operation.CanEdit || !RendererInfo.IsInteractive;
    private string? _error;
    private bool _accountEditor;
    private AccountData? _editingAccount;
    private IJSObjectReference? _module;
    private DotNetObjectReference<Workspace>? _reference;
    private bool IsRegister => AccountId.HasValue || navigation.Uri.TrimEnd('/').EndsWith("/accounts", StringComparison.Ordinal);

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
        var postDue = false;
        try
        {
            if (previous is not null) { await previous.CancelAsync(); }
            token.ThrowIfCancellationRequested();
            var (view, month) = await ReadWorkspaceAsync(planId, !IsRegister, token);
            token.ThrowIfCancellationRequested();
            if (generation != _requestGeneration) { return; }
            ApplyView(view, month);
            _operation = _operation.Loaded();
            // Interactive reads can complete after the first render. Post only after
            // the snapshot arrives, and never mutate the plan during static SSR.
            postDue = RendererInfo.IsInteractive && view.HasDueRecurring;
        }
        catch (BudgetRequestException exception) when (generation == _requestGeneration)
        {
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
            await ExecuteAsync(new PostRecurring(plan.Version, plan.Today));
        }
    }

    private async Task ExecuteAsync(PlanCommand command)
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
        _operation = WorkspaceOperation.Start(command);
        _error = null;
        try
        {
            await budgets.ExecuteAsync(planId, command, token);
            token.ThrowIfCancellationRequested();
            if (generation != _requestGeneration) { return; }
            _operation = _operation.Saved();
            var (view, month) = await ReadWorkspaceAsync(planId, !IsRegister, token);
            token.ThrowIfCancellationRequested();
            if (generation != _requestGeneration) { return; }
            ApplyView(view, month);
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

    private void ApplyView(PlanView view, BudgetMonth? month)
    {
        _view = view;
        Snapshot = view.Catalog;
        _preparedMonth = month is null ? null : new(view.Catalog.Id, view.Catalog.Version, month);
        _hasUpdates = false;
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
