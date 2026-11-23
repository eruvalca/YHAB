using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetBoard(IJSRuntime javascript, NavigationManager navigation)
{
    private const string LayoutModulePath = "./_content/YHAB.UI/Features/Budgeting/Components/BudgetBoard.razor.js";

    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    /// <summary>
    /// The command whose result the current plan reflects, and only that command can consume an
    /// open editor's draft. Null while nothing has been applied since the last command started.
    /// </summary>
    [Parameter] public PlanCommand? AppliedCommand { get; set; }
    /// <summary>
    /// The command whose uncertain write a refresh recovered. An editor closes its draft only
    /// when this is the save that draft submitted, so a recovered write it never sent leaves
    /// the typed values alone.
    /// </summary>
    [Parameter] public PlanCommand? RecoveredCommand { get; set; }
    [Parameter] public EventCallback OnAddAccount { get; set; }
    [Parameter] public bool Busy { get; set; }
    /// <summary>
    /// The workspace's account editor is open, and it pins its own actions to the
    /// bottom of a phone screen, so the docked shortcut has to step aside.
    /// </summary>
    [Parameter] public bool AccountEditing { get; set; }
    // A plain refresh keeps an open draft; a command closes it because the
    // command consumed the values the draft was editing.
    [Parameter] public bool ExternalRefresh { get; set; }
    [Parameter] public Func<DateOnly, CancellationToken, Task<BudgetMonth>>? LoadMonth { get; set; }
    /// <summary>
    /// The month a remounted board should show first, so a restored projection and the
    /// board's selection agree instead of triggering a second month read.
    /// </summary>
    [Parameter] public DateOnly? InitialMonth { get; set; }
    [Parameter] public RenderFragment? PlanActions { get; set; }

    private string? _error;
    private int _request;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Borrowed reference: CalculateAsync owns and disposes the source after its work completes; component disposal cancels its linked lifetime.")]
    private CancellationTokenSource? _loadCancellation;
    private bool IsBusy { get => Busy || field || _error is not null; set; }
    private DateOnly _month;
    private BudgetMonth _budget = default!;
    private PlanSnapshot? _budgetPlan;
    private long _version = -1;
    private PlanSnapshot? _loadedPlan;
    private bool _move;
    private PlanCommand? _moveCommand;
    private long _editorEpoch;
    private PlanCommand? _editorCommand;
    private bool _help;
    private bool _focusHelp;
    private ElementReference _helpPanel;
    private FluentButton? _monthStep;
    private bool _todayFocused;
    private bool _focusMonthStep;
    private BudgetCategoryTable? _categoryTable;
    private CategoryEditor? _categoryEditor;
    private CategoryEditor? _inlineEditor;
    private Guid? _inlineEditorCategory;
    private MoveMoneyEditor? _moveEditor;
    private CategoryData? _category;
    // Below the desktop breakpoint the editor opens beneath its row instead of in
    // a pane after the table, so the row that was tapped stays in view.
    private bool _inlineDetails;
    private IJSObjectReference? _layoutModule;
    // Inline row controls keep focus, so only a selection that owns focus moves it into the editor.
    private bool _focusCategory = true;
    // The details draft belongs to the selection, not to whichever surface renders it, so a
    // reorder that recreates the row or a resize that moves the editor restores the typed values.
    private CategoryEditorDraft? _categoryDraft;
    private Guid? _categoryDraftCategory;
    private DateOnly _categoryDraftMonth;
    private long _categoryDraftEpoch = -1;
    // Bumped by every selection and close, so a layout lookup that finishes late cannot
    // override a newer choice or reopen a closed editor.
    private long _selectionEpoch;
    private CategoryMonth? EditingMonth => _budget.Categories.SingleOrDefault(item => item.Category.Id == _category?.Id);
    private bool HasAccounts => Plan.Accounts.Count > 0;
    // The Today action only changes what the sticky summary shows while another month is open.
    private bool IsCurrentMonth => _budget.Month == BudgetFacts.Month(Plan.Today);
    private bool HasOpenAccounts => Plan.Accounts.Any(item => !item.Closed);
    // A phone's editor pins its own heading to the top of the viewport, so the
    // sticky summary steps aside instead of covering it. The inline category
    // editor scrolls with its row and pins nothing, so the summary stays.
    private bool IsEditing => _move || _category is not null;
    private bool IsPinnedEditor => _move || (_category is not null && !_inlineDetails);
    // The category whose details draft has unsaved edits, whichever placement is open.
    // A closed editor's reference outlives it, so the open category gates this.
    private Guid? DirtyCategoryId => (_category, _inlineDetails ? _inlineEditor : _categoryEditor) switch
    {
        ({ } category, { IsDirty: true }) => category.Id,
        _ => null,
    };
    // A phone keeps the sticky summary on screen, so this action docks to the
    // viewport instead of adding a row that would shrink the category table, and
    // it steps aside for every board editor so it cannot cover their actions.
    private string AddTransactionClass => IsEditing || AccountEditing ? "board-add-transaction is-editing" : "board-add-transaction";
    // Switching editors would drop the other editor's draft without its discard
    // prompt, so each entry point stays closed while one is already open.
    private string? TransactionHint => IsEditing || AccountEditing ? "Finish or close the open editor first." : null;
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
    private string? MoveMoneyHint => Plan.Categories.Count == 0
        ? "Add at least one category before moving money."
        : TransactionHint;

    protected override async Task OnParametersSetAsync()
    {
        if (_month == default) { _month = InitialMonth ?? BudgetFacts.Month(Plan.Today); }
        if (_loadedPlan?.Id != Plan.Id || _version != Plan.Version || _budget is null || (_error is not null && !ReferenceEquals(_loadedPlan, Plan)))
        {
            if (_loadedPlan?.Id != Plan.Id) { _help = false; CloseEditors(); }
            else
            {
                // A command replaces an editor's baseline only when it consumed its draft, and only
                // that editor's own submitted save or removal counts. An external refresh, an
                // assignment, a toolbar Undo/Redo, or another category's create or inline rename
                // keeps the open draft mounted so its typed values are not recreated.
                if (!ExternalRefresh)
                {
                    // Only the move editor's own submitted save settles its draft, so an unrelated
                    // successful write that refreshes the plan leaves a dirty draft mounted. A clean
                    // draft still closes with the refresh.
                    var settledMove = _moveCommand is not null
                        && (ReferenceEquals(AppliedCommand, _moveCommand) || ReferenceEquals(RecoveredCommand, _moveCommand));
                    if (settledMove || _moveEditor is not { IsDirty: true }) { CloseMove(); }
                    if (ReferenceEquals(AppliedCommand, _editorCommand) && AppliedCommand is SaveCategory or RemoveCategory)
                    {
                        _editorEpoch++;
                        // The save remounts the editor, so its replacement surface takes focus back from
                        // an inline selection that had deliberately left focus in the row.
                        _focusCategory = true;
                    }
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
        if (IsDisposed) { return; }
        if (_focusHelp)
        {
            _focusHelp = false;
            await _helpPanel.FocusAsync();
        }
        if (_focusMonthStep)
        {
            _focusMonthStep = false;
            if (_monthStep is not null) { await _monthStep.Element.FocusAsync(); }
        }
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
            if (request == _request)
            {
                _budget = result;
                _budgetPlan = Plan;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed or superseded read. */ }
        catch (OperationCanceledException) { if (request == _request) { _error = "Loading this month timed out. Refresh your plan to try again."; } }
        catch (BudgetRequestException exception) { if (!token.IsCancellationRequested && request == _request) { _error = exception.Message; } }
        catch (HttpRequestException) { if (!token.IsCancellationRequested && request == _request) { _error = "Unable to load this month. Refresh your plan to try again."; } }
        finally
        {
            if (ReferenceEquals(_loadCancellation, operation)) { _loadCancellation = null; }
            if (!IsDisposed && request == _request)
            {
                if (_error is not null && _budget is not null) { _month = _budget.Month; }
                IsBusy = false;
            }
        }
    }
    private async Task PreviousAsync() { _month = _month.AddMonths(-1); await CalculateAsync(); }
    private async Task NextAsync() { _month = _month.AddMonths(1); await CalculateAsync(); }
    private async Task CurrentAsync()
    {
        _month = BudgetFacts.Month(Plan.Today);
        await CalculateAsync();
        // The action unmounts when the current month arrives, which would leave a
        // keyboard user with no focus indicator. Hand focus to the pager unless
        // they moved elsewhere while the read was in flight. A failed read keeps
        // Today mounted, so it keeps focus ownership for an immediate retry; only
        // the successful handoff clears it, since the action can no longer own it.
        if (_todayFocused && IsCurrentMonth)
        {
            _todayFocused = false;
            _focusMonthStep = true;
        }
    }
    private void TodayFocused() => _todayFocused = true;
    private void TodayBlurred() => _todayFocused = false;
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
    // Transaction entry lives in the register, where desktop users can work in a row
    // and phones can expand the same draft into a stacked form.
    private void ShowTransaction()
    {
        if (!HasOpenAccounts || IsEditing || AccountEditing) { return; }
        navigation.NavigateTo($"/plans/{Plan.Id}/accounts?new=true");
    }
    // Track the move editor's own command so an unrelated write cannot settle its draft.
    private Task SendMoveCommandAsync(PlanCommand command)
    {
        _moveCommand = command;
        return OnCommand.InvokeAsync(command);
    }
    private void CloseMove() { _move = false; _moveEditor = null; _moveCommand = null; }
    private void CloseCategory()
    {
        _selectionEpoch++;
        _category = null;
        _categoryEditor = null;
        _inlineEditor = null;
        _inlineEditorCategory = null;
        _categoryDraft = null;
    }
    private Task ShowHelpAsync()
    {
        _help = true;
        _focusHelp = true;
        return Task.CompletedTask;
    }
    private void HideHelp() => _help = false;
    private Task AddCategoryAsync() { _help = false; _categoryTable?.BeginAddCategory(); return Task.CompletedTask; }
    private async Task EditCategoryAsync(CategorySelection selection)
    {
        // An inline row action must not discard another draft, whether that is a category's unsaved
        // edits or an open move-money draft; the workspace convention is to finish or close the open
        // editor first. The action that triggered the selection still runs.
        if (WouldDiscardDraft(selection)) { return; }
        // A continuation that finishes after a newer selection or a close must not win.
        var epoch = ++_selectionEpoch;
        // The placement follows the breakpoint the stylesheet already stacks the
        // workspace on, so a wide screen keeps its side pane and a narrow one opens
        // the editor beneath the row instead of after the whole table.
        var inline = await InlineDetailsAsync();
        if (IsDisposed || epoch != _selectionEpoch) { return; }
        // The lookup waits on the browser, so the open editor can gain unsaved edits under it.
        // Replacing that editor now would drop them without ever offering the discard prompt.
        if (WouldDiscardDraft(selection)) { return; }
        // The lookup waits on the browser, so the plan can refresh under it. Select the record the
        // current plan has rather than the snapshot the click captured.
        var category = Plan.Categories.SingleOrDefault(item => item.Id == selection.Category.Id);
        if (category is null) { return; }
        _inlineDetails = inline;
        CloseMove();
        _category = category;
        _focusCategory = selection.FocusEditor;
    }
    // A selection that would replace an editor holding unsaved edits has to wait for the person to
    // finish or close it; the board never discards a draft on another row's action.
    private bool WouldDiscardDraft(CategorySelection selection)
        => !selection.FocusEditor
            && (_move && _moveEditor is { IsDirty: true }
                || DirtyCategoryId is { } dirtyCategory && dirtyCategory != selection.Category.Id);
    // The selection's draft, shared by both placements, so moving the editor never loses what was
    // typed. It is replaced when the category, the month, or a settled save's remount changes.
    private CategoryEditorDraft? CategoryDraft()
    {
        if (_category is not { } category) { return null; }
        if (_categoryDraft is null
            || _categoryDraftCategory != category.Id
            || _categoryDraftMonth != _budget.Month
            || _categoryDraftEpoch != _editorEpoch)
        {
            _categoryDraft = new();
            _categoryDraftCategory = category.Id;
            _categoryDraftMonth = _budget.Month;
            _categoryDraftEpoch = _editorEpoch;
        }
        return _categoryDraft;
    }
    // The mounted inline editor reports itself so the selection guard can see its live draft,
    // including uncommitted amount text.
    private void RegisterInlineEditor(Guid categoryId, CategoryEditor? editor)
    {
        if (editor is null)
        {
            // A row that is leaving can report after the next row has already registered; only
            // the stored editor's own category may clear it.
            if (_inlineEditorCategory == categoryId) { _inlineEditor = null; _inlineEditorCategory = null; }
            return;
        }
        // Only the row for the open selection may become the guard's editor.
        if (_category?.Id != categoryId) { return; }
        _inlineEditor = editor;
        _inlineEditorCategory = categoryId;
    }
    // A row's move controls replace its details editor only when that editor has no unsaved edits.
    private bool TryDismissCategory()
    {
        if (DirtyCategoryId is not null) { return false; }
        CloseCategory();
        // The row invokes this from its own handler, so the board has to render again for the
        // cleared selection to reach the table.
        StateHasChanged();
        return true;
    }
    private async Task<bool> InlineDetailsAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            _layoutModule ??= await javascript.InvokeAsync<IJSObjectReference>("import", token, LayoutModulePath);
            token.ThrowIfCancellationRequested();
            return await _layoutModule.InvokeAsync<bool>("inlineCategoryDetails", token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (JSDisconnectedException) { return false; }
        catch (JSException) { return false; }
    }
    protected override async ValueTask DisposeCoreAsync()
    {
        if (_layoutModule is not null)
        {
            try { await _layoutModule.DisposeAsync(); }
            catch (JSDisconnectedException) { /* The circuit already released its JavaScript references. */ }
        }

        await base.DisposeCoreAsync();
    }
    private void CloseEditors()
    {
        _selectionEpoch++;
        CloseMove();
        _category = null;
        _categoryEditor = null;
        _inlineEditor = null;
        _inlineEditorCategory = null;
        _categoryDraft = null;
    }
}
