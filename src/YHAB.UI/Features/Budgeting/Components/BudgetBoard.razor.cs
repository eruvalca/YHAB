using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetBoard
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    /// <summary>
    /// The command whose result the current plan reflects, and only that command can consume an
    /// open editor's draft. Null while nothing has been applied since the last command started.
    /// </summary>
    [Parameter] public PlanCommand? AppliedCommand { get; set; }
    [Parameter] public EventCallback OnAddAccount { get; set; }
    [Parameter] public bool Busy { get; set; }
    // A plain refresh keeps an open draft; a command closes it because the
    // command consumed the values the draft was editing.
    [Parameter] public bool ExternalRefresh { get; set; }
    [Parameter] public Func<DateOnly, CancellationToken, Task<BudgetMonth>>? LoadMonth { get; set; }
    [Parameter] public RenderFragment? PlanActions { get; set; }

    private string? _error;
    private int _request;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Borrowed reference: CalculateAsync owns and disposes the source after its work completes; component disposal cancels its linked lifetime.")]
    private CancellationTokenSource? _loadCancellation;
    private bool IsBusy { get => Busy || field || _error is not null; set; }
    private DateOnly _month;
    private BudgetMonth _budget = default!;
    private long _version = -1;
    private PlanSnapshot? _loadedPlan;
    private bool _move;
    private long _editorEpoch;
    private PlanCommand? _editorCommand;
    private bool _help;
    private bool _focusHelp;
    private ElementReference _helpPanel;
    private BudgetCategoryTable? _categoryTable;
    private CategoryData? _category;
    private CategoryMonth? EditingMonth => _budget.Categories.SingleOrDefault(item => item.Category.Id == _category?.Id);
    private bool HasAccounts => Plan.Accounts.Count > 0;
    private bool HasFundableNeed => _budget.Categories.Any(item =>
        !item.Snoozed && !item.Category.Hidden
        && !Plan.Groups.Any(group => group.Id == item.Category.GroupId && group.Hidden)
        && (item.TargetNeeded > 0 || item.Available < 0));
    private decimal StillNeeded => _budget.Categories.Sum(item => Math.Max(item.TargetNeeded, -item.Available));
    // TargetNeeded already includes restoring a negative Available, so count overspending first and
    // attribute only the remaining need to targets. The parts always add up to StillNeeded.
    private string? StillNeededBreakdown
    {
        get
        {
            decimal targets = 0;
            decimal overspending = 0;
            foreach (var item in _budget.Categories)
            {
                var overspent = Math.Max(-item.Available, 0);
                var target = Math.Max(item.TargetNeeded, 0);
                overspending += overspent;
                targets += Math.Max(target - overspent, 0);
            }

            return (targets, overspending) switch
            {
                (0, 0) => null,
                (_, 0) => $"{BudgetFacts.Money(targets)} for targets",
                (0, _) => $"{BudgetFacts.Money(overspending)} to cover overspending",
                _ => $"{BudgetFacts.Money(targets)} for targets, {BudgetFacts.Money(overspending)} to cover overspending",
            };
        }
    }
    private string? FundTargetsHint
    {
        get
        {
            if (_budget.ReadyToAssign <= 0) { return "Add money to Ready to assign before funding targets."; }
            return HasFundableNeed ? null : "Every target is funded, hidden, or snoozed. Nothing to fund this month.";
        }
    }
    private string? MoveMoneyHint => Plan.Categories.Count == 0 ? "Add at least one category before moving money." : null;

    protected override async Task OnParametersSetAsync()
    {
        if (_month == default) { _month = BudgetFacts.Month(Plan.Today); }
        if (_loadedPlan?.Id != Plan.Id || _version != Plan.Version || _budget is null || (_error is not null && !ReferenceEquals(_loadedPlan, Plan)))
        {
            if (_loadedPlan?.Id != Plan.Id) { _help = false; CloseEditors(); }
            else
            {
                // A command replaces the editor's baseline only when it consumed its draft, and only
                // this editor's own submitted save or removal counts. An external refresh, an
                // assignment, a toolbar Undo/Redo, or another category's create or inline rename
                // keeps the open draft mounted so its typed values are not recreated.
                if (!ExternalRefresh)
                {
                    _move = false;
                    if (ReferenceEquals(AppliedCommand, _editorCommand) && AppliedCommand is SaveCategory or RemoveCategory) { _editorEpoch++; }
                }
                _category = Plan.Categories.SingleOrDefault(item => item.Id == _category?.Id);
            }
            _version = Plan.Version;
            _loadedPlan = Plan;
            await CalculateAsync();
        }
    }
    // Opening help moves focus to the panel so keyboard users land on the explanation instead of the next control.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusHelp || IsDisposed) { return; }
        _focusHelp = false;
        await _helpPanel.FocusAsync();
    }
    private async Task CalculateAsync()
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
            var result = LoadMonth is null ? BudgetCalculator.Calculate(Plan, _month, Plan.Today) : await LoadMonth(_month, token);
            token.ThrowIfCancellationRequested();
            if (request == _request) { _budget = result; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed or superseded read. */ }
        catch (OperationCanceledException) { if (request == _request) { _error = "Loading this month timed out. Refresh your plan to try again."; } }
        catch (BudgetRequestException exception) { if (!token.IsCancellationRequested && request == _request) { _error = exception.Message; } }
        catch (HttpRequestException) { if (!token.IsCancellationRequested && request == _request) { _error = "Unable to load this month. Refresh your plan to try again."; } }
        finally
        {
            if (ReferenceEquals(_loadCancellation, operation)) { _loadCancellation = null; }
            if (!IsDisposed && request == _request) { IsBusy = false; }
        }
    }
    private async Task PreviousAsync() { _month = _month.AddMonths(-1); await CalculateAsync(); }
    private async Task NextAsync() { _month = _month.AddMonths(1); await CalculateAsync(); }
    private async Task CurrentAsync() { _month = BudgetFacts.Month(Plan.Today); await CalculateAsync(); }
    private Task AutoAssignAsync() => OnCommand.InvokeAsync(new AutoAssign(Plan.Version, _month));

    // Remember the exact command the mounted target editor submitted, so the board can tell its own
    // save apart from another category's create or inline rename that uses the same command type.
    private Task SendCategoryCommandAsync(PlanCommand command)
    {
        _editorCommand = command;
        return OnCommand.InvokeAsync(command);
    }
    private Task AddAccountAsync() => OnAddAccount.InvokeAsync();
    private void ShowMove() { if (MoveMoneyHint is not null) { return; } CloseEditors(); _move = true; }
    private Task ShowHelpAsync()
    {
        _help = true;
        _focusHelp = true;
        return Task.CompletedTask;
    }
    private void HideHelp() => _help = false;
    private Task AddCategoryAsync() { _help = false; _categoryTable?.BeginAddCategory(); return Task.CompletedTask; }
    private void EditCategory(CategoryData category) { _move = false; _category = category; }
    private void CloseEditors() { _move = false; _category = null; }
}
