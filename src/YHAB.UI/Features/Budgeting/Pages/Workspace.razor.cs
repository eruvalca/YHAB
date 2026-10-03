using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Workspace(IBudgetClient budgets, NavigationManager navigation, IJSRuntime javascript) : IAsyncDisposable
{
    [Parameter] public Guid PlanId { get; set; }
    [Parameter] public Guid? AccountId { get; set; }
    // The full ledger can exceed SignalR's startup message limit. Each renderer
    // loads its own owner-scoped snapshot instead of round-tripping it in prerender state.
    private PlanSnapshot? Snapshot { get; set; }
    private bool _busy;
    private bool IsBusy => _busy || !RendererInfo.IsInteractive;
    private string? _error;
    private string _status = "All changes saved";
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
        }
    }

    private async Task RefreshAsync()
    {
        _error = null;
        try
        {
            Snapshot = await budgets.ReadAsync(PlanId);
            // Interactive reads can complete after the first render. Post only after
            // the snapshot arrives, and never mutate the plan during static SSR.
            if (RendererInfo.IsInteractive && Snapshot is { } plan && plan.Transactions.Any(item => item.Repeat != RepeatFrequency.None && item.Date <= plan.Today
                && !plan.Accounts.Any(account => account.Closed && (account.Id == item.AccountId || account.Id == item.TransferAccountId))))
            {
                await ExecuteAsync(new PostRecurring(plan.Version, plan.Today));
            }
        }
        catch (BudgetRequestException exception)
        {
            _error = exception.Message;
        }
        catch (HttpRequestException)
        {
            _error = "We couldn't reach your plan. Check your connection and refresh.";
        }
    }

    private async Task ExecuteAsync(PlanCommand command)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        _error = null;
        try
        {
            await budgets.ExecuteAsync(PlanId, command);
            Snapshot = await budgets.ReadAsync(PlanId);
            _status = command switch { UndoChange => "Change undone", RedoChange => "Change restored", _ => "All changes saved" };
            if (command is SaveAccount)
            {
                CloseAccount();
            }
        }
        catch (BudgetRequestException exception)
        {
            _error = exception.Status == 409 ? "This plan changed in another tab. Refresh it, then try your change again." : exception.Message;
        }
        catch (HttpRequestException)
        {
            _error = "Connection interrupted. Refresh to check whether your change was saved before trying again.";
        }
        finally
        {
            _busy = false;
        }
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
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("disconnect");
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
