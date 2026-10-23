using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class FirstRunChecklist(IJSRuntime javascript)
{
    private const string ModulePath = "./_content/YHAB.UI/first-run.js";

    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public BudgetMonth Budget { get; set; } = default!;
    [Parameter] public bool Hidden { get; set; }
    [Parameter] public EventCallback OnAddAccount { get; set; }
    [Parameter] public EventCallback OnAddCategory { get; set; }
    [Parameter] public EventCallback OnShowHelp { get; set; }
    [Parameter] public bool Busy { get; set; }

    private IJSObjectReference? _module;
    private bool _ready;
    private bool _dismissed;
    private bool _assignmentPersisted;

    private bool HasAccounts => Plan.Accounts.Count > 0;
    private bool HasCategories => Plan.Categories.Count > 0;
    private bool HasAssigned => _assignmentPersisted || Budget.Assigned > 0;
    private bool ChecklistVisible => _ready && !_dismissed && CurrentStep != 0;
    private bool PanelVisible => !Hidden && (!HasAccounts || ChecklistVisible);
    private int CurrentStep
    {
        get
        {
            if (!HasAccounts) { return 1; }
            if (!HasCategories) { return 2; }
            return HasAssigned ? 0 : 3;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var token = LifetimeToken;
            try
            {
                var module = await javascript.InvokeAsync<IJSObjectReference>("import", token, ModulePath);
                if (IsDisposed)
                {
                    await module.DisposeAsync();
                    return;
                }

                _module = module;
                _dismissed = await module.InvokeAsync<bool>("isDismissed", token, Plan.Id);
                _assignmentPersisted = await module.InvokeAsync<bool>("hasAssigned", token, Plan.Id);
                _ready = true;
                StateHasChanged();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* The component was disposed while reading the saved state. */ }
            catch (JSDisconnectedException) { /* The circuit closed before the saved state could be read. */ }
        }

        await PersistAssignmentAsync();
    }

    private async Task PersistAssignmentAsync()
    {
        if (_module is null || _assignmentPersisted || Budget.Assigned <= 0) { return; }
        _assignmentPersisted = true;
        var token = LifetimeToken;
        try { await _module.InvokeVoidAsync("completeAssignment", token, Plan.Id); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed after completion was observed. */ }
        catch (JSDisconnectedException) { /* The circuit closed before completion was saved. */ }
    }

    protected override async ValueTask DisposeCoreAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { /* The circuit already released its JavaScript references. */ }
        }

        await base.DisposeCoreAsync();
    }

    private bool IsCurrent(int step) => CurrentStep == step;
    private static string Complete(bool complete) => complete ? "true" : "false";
    private string Current(int step) => IsCurrent(step) ? "true" : "false";
    private string? StepStatus(int step) => IsCurrent(step) ? "step" : null;
    private string StepLabel(bool complete, int step)
    {
        if (complete) { return "Completed: "; }
        return IsCurrent(step) ? "Current step: " : string.Empty;
    }
    private static string Marker(bool complete, int step) => complete ? "✓" : step.ToString(CultureInfo.InvariantCulture);

    private async Task DismissAsync()
    {
        _dismissed = true;
        if (_module is null) { return; }
        var token = LifetimeToken;
        try { await _module.InvokeVoidAsync("dismiss", token, Plan.Id); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed after the checklist was hidden. */ }
        catch (JSDisconnectedException) { /* The checklist stays hidden for this render. */ }
    }

    private Task AddAccountAsync() => OnAddAccount.InvokeAsync();
    private Task AddCategoryAsync() => OnAddCategory.InvokeAsync();
    private Task ShowHelpAsync() => OnShowHelp.InvokeAsync();
}
