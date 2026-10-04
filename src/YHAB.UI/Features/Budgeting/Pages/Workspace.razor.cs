using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Workspace(IBudgetClient budgets, NavigationManager navigation, IJSRuntime javascript) : IAsyncDisposable
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
    private readonly CancellationTokenSource _lifetime = new();
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
    private void LocationChanged(object? sender, LocationChangedEventArgs args) => _ = InvokeAsync(StateHasChanged);

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
        if (firstRender)
        {
            _module = await javascript.InvokeAsync<IJSObjectReference>("import", "./_content/YHAB.UI/budget-workspace.js");
            _reference = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("connect", _reference);
            _poll = WatchRevisionAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_busy) { return; }
        var planId = PlanId;
        var generation = ++_requestGeneration;
        _busy = true;
        _error = null;
        var postDue = false;
        try
        {
            var (view, month) = await ReadWorkspaceAsync(planId, !IsRegister);
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
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { /* Normal workspace disposal. */ }
        finally
        {
            if (generation == _requestGeneration) { _busy = false; }
        }
        if (postDue && generation == _requestGeneration && Snapshot is { } plan)
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
        var planId = PlanId;
        var generation = ++_requestGeneration;
        _operation = WorkspaceOperation.Start(command);
        _error = null;
        try
        {
            await budgets.ExecuteAsync(planId, command, _lifetime.Token);
            if (generation != _requestGeneration) { return; }
            _operation = _operation.Saved();
            var (view, month) = await ReadWorkspaceAsync(planId, !IsRegister);
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
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { /* Normal workspace disposal. */ }
        finally
        {
            if (generation == _requestGeneration) { _busy = false; }
        }
    }

    private Task RetryAsync() => _operation is { CanRetry: true, Pending: { } command } ? ExecuteAsync(command) : Task.CompletedTask;
    private Task<BudgetMonth> LoadMonthAsync(DateOnly month)
    {
        _month = month;
        return _preparedMonth is { } prepared && prepared.PlanId == PlanId && prepared.Version == Snapshot!.Version && prepared.Budget.Month == month
            ? Task.FromResult(prepared.Budget)
            : budgets.ReadMonthAsync(PlanId, month, Snapshot!.Version, _lifetime.Token);
    }

    private async Task<(PlanView View, BudgetMonth? Month)> ReadWorkspaceAsync(Guid planId, bool includeMonth)
    {
        var month = _month;
        // Publish matching navigation and month values together. Only read conflicts
        // are retried, at most three times; a committed command is never resent.
        var conflicts = 0;
        while (true)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            try
            {
                if (includeMonth)
                {
                    var result = await budgets.ReadWorkspaceAsync(planId, month, _lifetime.Token);
                    return (result.View, result.Month);
                }
                return (await budgets.ReadViewAsync(planId, _lifetime.Token), null);
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
    private Task<RegisterPage> LoadRegisterAsync(RegisterQuery query) => budgets.ReadRegisterAsync(PlanId, query with { Version = Snapshot!.Version }, _lifetime.Token);
    private Task<IReadOnlyList<string>> LoadPayeesAsync() => budgets.ReadPayeesAsync(PlanId, _lifetime.Token);

    private async Task WatchRevisionAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                if (_busy || Snapshot is not { } snapshot) { continue; }
                try
                {
                    var revision = await budgets.ReadRevisionAsync(snapshot.Id, _lifetime.Token);
                    if (Snapshot?.Id == snapshot.Id && Snapshot.Version < revision)
                    {
                        _hasUpdates = true;
                        await InvokeAsync(StateHasChanged);
                    }
                }
                catch (BudgetRequestException) { /* An explicit refresh presents authentication errors. */ }
                catch (HttpRequestException) { /* Preserve the user's inputs during a temporary disconnection. */ }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { /* The workspace was disposed. */ }
    }

    [JSInvokable]
    public Task HistoryShortcutAsync(bool redo) => InvokeAsync(async () =>
    {
        await (redo ? RedoAsync() : UndoAsync());
        StateHasChanged();
    });
    private Task UndoAsync() => Snapshot is { CanUndo: true } plan ? ExecuteAsync(new UndoChange(plan.Version)) : Task.CompletedTask;
    private Task RedoAsync() => Snapshot is { CanRedo: true } plan ? ExecuteAsync(new RedoChange(plan.Version)) : Task.CompletedTask;
    private void AddAccount() { _editingAccount = null; _accountEditor = true; }
    private void EditAccount(AccountData account) { _editingAccount = account; _accountEditor = true; }
    private void CloseAccount() => _accountEditor = false;

    public async ValueTask DisposeAsync()
    {
        navigation.LocationChanged -= LocationChanged;
        await _lifetime.CancelAsync();
        if (_poll is not null) { await _poll; }
        _lifetime.Dispose();
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("disconnect", CancellationToken.None);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // A disconnected server circuit has already released its browser listeners.
        }
        _reference?.Dispose();
    }
}
