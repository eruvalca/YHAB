using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetCategoryTable
{
    private const string ListPrefix = "category-list-";
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public BudgetMonth Budget { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback<CategorySelection> OnSelect { get; set; }
    [Parameter] public Guid? SelectedCategoryId { get; set; }
    [Parameter] public bool Busy { get; set; }
    /// <summary>
    /// The details editor opens beneath its row rather than in a pane beside the
    /// table, so the row stays visible on phone and tablet widths.
    /// </summary>
    [Parameter] public bool InlineEditor { get; set; }
    [Parameter] public EventCallback OnCloseEditor { get; set; }
    /// <summary>
    /// Carries the editor's own commands, so a save this editor submitted settles
    /// its draft the way the side pane's does.
    /// </summary>
    [Parameter] public EventCallback<PlanCommand> OnEditorCommand { get; set; }
    /// <summary>
    /// The selection that opened the editor owns focus, so only a selection that
    /// claims it moves focus into the editor.
    /// </summary>
    [Parameter] public bool FocusEditor { get; set; } = true;
    /// <summary>
    /// Changes when a save settles the open editor's draft, remounting it against
    /// the plan the command produced.
    /// </summary>
    [Parameter] public long EditorEpoch { get; set; }
    /// <summary>
    /// Reports the mounted inline editor, with its category, so the board's selection guard
    /// can see its live draft, including uncommitted amount text. A plain delegate, because
    /// the board reads it synchronously and it must not schedule another render.
    /// </summary>
    [Parameter] public Action<Guid, CategoryEditor?>? OnEditorRegistered { get; set; }
    /// <summary>
    /// The selection's details draft, owned by the board so both placements and a recreated
    /// row restore the same typed values.
    /// </summary>
    [Parameter] public CategoryEditorDraft? CategoryDraft { get; set; }
    /// <summary>
    /// Closes the selected details editor unless it has unsaved edits, so a row's move
    /// controls can reuse its place.
    /// </summary>
    [Parameter] public Func<bool>? DismissSelection { get; set; }
    private static readonly string _stillNeeded = BudgetGlossary.Label(BudgetTerm.StillNeeded);
    private static readonly string[] _filters = ["All categories", _stillNeeded, "Overspent", "Available", "Hidden"];
    private string _filter = "All categories";
    private readonly HashSet<Guid> _collapsed = [];
    private readonly Dictionary<Guid, InlineNameDraft> _drafts = [];
    private Dictionary<Guid, CategoryMonth> _rows = [];
    // Shows a dropped order while its save is in flight; the confirmed plan replaces it.
    private Dictionary<Guid, Guid[]> _pending = [];
    private Guid? _dragGroup;
    private Guid? _movingGroup;
    private Guid? _movingCategory;
    private Guid? _selectedCategory;
    private Guid? _restoreFocusCategory;
    private Guid? _newCategoryGroup;
    private bool _newGroup;
    private long _version = -1;
    private ElementReference _table;
    private TableInterop? _interop;
    private readonly CancellationTokenSource _disposal = new();
    private GroupData[] Groups { get; set; } = [];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A focus request belongs to the render that closed the editor. Clearing it
        // here drops a request no row could consume and keeps a later filter or
        // group change from moving focus unexpectedly.
        _restoreFocusCategory = null;
        if (InlineEditor && !ShowsSelectedCategory())
        {
            // A filter, a collapsed group, or a saved hidden status took the selected row out
            // of the table. Its editor unmounted with it, so the board has to drop the
            // selection; otherwise the open-editor hints block the other board actions.
            await OnCloseEditor.InvokeAsync();
        }
        if (firstRender)
        {
            _interop = new(JS);
            await _interop.InitializeAsync(_table, _disposal.Token);
        }
    }

    private bool ShowsSelectedCategory()
    {
        if (SelectedCategoryId is not { } selected) { return true; }
        foreach (var group in Groups)
        {
            if (_collapsed.Contains(group.Id)) { continue; }
            var rows = Rows(group.Id);
            if (!ShowGroup(group, rows.Length)) { continue; }
            if (rows.Any(item => item.Category.Id == selected)) { return true; }
        }
        return false;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _disposal.CancelAsync();
        try
        {
            if (_interop is not null) { await _interop.DisposeAsync(); }
        }
        finally
        {
            _disposal.Dispose();
        }
    }

    protected override void OnParametersSet()
    {
        Groups = Plan.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray();
        _rows = Budget.Categories.ToDictionary(item => item.Category.Id);
        if (_selectedCategory != SelectedCategoryId)
        {
            // A row shows one panel at a time: selecting it collapses the move
            // controls it had open, and closing the editor hands focus back to the
            // row that opened it instead of dropping focus on the document body.
            _restoreFocusCategory = SelectedCategoryId is null ? _selectedCategory : null;
            _movingCategory = null;
            _selectedCategory = SelectedCategoryId;
        }
        if (_version != Plan.Version)
        {
            _version = Plan.Version;
            CancelCreate();
            ClearDrag();
            foreach (var id in _drafts.Keys.Where(id => !_rows.ContainsKey(id)).ToArray()) { _drafts.Remove(id); }
        }
    }

    private static string ListId(Guid group) => $"{ListPrefix}{group:N}";

    // The sortable wrappers are unkeyed, so a reorder recreates rows; keep the open editor state here.
    private InlineNameDraft Draft(Guid category)
    {
        if (!_drafts.TryGetValue(category, out var draft)) { _drafts[category] = draft = new(); }
        return draft;
    }

    // Plan order is authoritative because the budget month is recalculated after a plan change arrives.
    private Guid[] Order(Guid group) => _pending.TryGetValue(group, out var pending) ? pending
        : Plan.Categories.Where(item => item.GroupId == group && !_pending.Values.Any(ids => ids.Contains(item.Id)))
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Id).Select(item => item.Id).ToArray();

    private CategoryMonth[] Rows(Guid group)
    {
        var hiddenGroup = Plan.Groups.Any(value => value.Id == group && value.Hidden);
        return Order(group).Select(id => _rows.GetValueOrDefault(id)).OfType<CategoryMonth>()
            .Where(item => (string.Equals(_filter, "Hidden", StringComparison.Ordinal) ? item.Category.Hidden || hiddenGroup : !item.Category.Hidden && !hiddenGroup)
                && MatchesView(item))
            .ToArray();
    }

    private bool MatchesView(CategoryMonth item)
    {
        if (string.Equals(_filter, _stillNeeded, StringComparison.Ordinal)) { return item.TargetNeeded > 0 || item.Available < 0; }
        return _filter switch { "Overspent" => item.Available < 0, "Available" => item.Available > 0, _ => true };
    }

    private bool ShowGroup(GroupData group, int count) => count > 0 || _newCategoryGroup == group.Id
        || (string.Equals(_filter, "All categories", StringComparison.Ordinal) && !group.Hidden)
        || (string.Equals(_filter, "Hidden", StringComparison.Ordinal) && group.Hidden);
    private void ToggleGroup(Guid id) { if (!_collapsed.Add(id)) { _collapsed.Remove(id); } }
    private void ToggleAll() { if (_collapsed.Count > 0) { _collapsed.Clear(); } else { _collapsed.UnionWith(Plan.Groups.Select(item => item.Id)); } }
    private void ToggleGroupMove(Guid id) => _movingGroup = _movingGroup == id ? null : id;
    private Task ToggleCategoryMoveAsync(Guid id)
    {
        if (_movingCategory == id)
        {
            _movingCategory = null;
            return Task.CompletedTask;
        }
        // One panel per row: the move controls replace the details editor. A dirty draft keeps
        // the editor open and the controls closed, the way another editor's entry point waits.
        if (InlineEditor && SelectedCategoryId == id)
        {
            if (DismissSelection is null || !DismissSelection()) { return Task.CompletedTask; }
            // The board reports no selection on the next render; remembering that here keeps
            // the selection-change branch from collapsing the controls we are opening.
            _selectedCategory = null;
        }
        _movingCategory = id;
        return Task.CompletedTask;
    }
    private void AddGroup() { CancelCreate(); _filter = "All categories"; _newGroup = true; }
    internal void BeginAddCategory()
    {
        var group = Groups.FirstOrDefault(item => !item.Hidden);
        if (group is null) { AddGroup(); }
        else { AddToGroup(group.Id); }
    }
    private void AddCategory() => AddToGroup(Groups.First(item => !item.Hidden).Id);
    private void AddToGroup(Guid id)
    {
        CancelCreate();
        _filter = Plan.Groups.Single(item => item.Id == id).Hidden ? "Hidden" : "All categories";
        _newCategoryGroup = id;
        _collapsed.Remove(id);
    }
    private void CancelCreate() { _newGroup = false; _newCategoryGroup = null; }
    private Task CreateGroupAsync(string name) => OnCommand.InvokeAsync(new SaveGroup(Plan.Version, new(Guid.Empty, name, Plan.Groups.Select(item => item.SortOrder).DefaultIfEmpty(-1).Max() + 1)));
    private Task CreateCategoryAsync(string name) => OnCommand.InvokeAsync(new SaveCategory(Plan.Version, new(Guid.Empty, _newCategoryGroup!.Value, name, string.Empty,
        Plan.Categories.Where(item => item.GroupId == _newCategoryGroup).Select(item => item.SortOrder).DefaultIfEmpty(-1).Max() + 1, false, null, null)));
    private Task RenameGroupAsync(GroupData group, string name) => OnCommand.InvokeAsync(new SaveGroup(Plan.Version, group with { Name = name }));
    private Task HideGroupAsync(GroupData group) => OnCommand.InvokeAsync(new SaveGroup(Plan.Version, group with { Hidden = !group.Hidden }));
    private Task MoveGroupAsync(GroupData group, int direction)
    {
        var groups = Groups;
        var index = Array.IndexOf(groups, group);
        var before = direction < 0 ? groups[index - 1].Id : groups.ElementAtOrDefault(index + 2)?.Id;
        return OnCommand.InvokeAsync(new ReorderGroup(Plan.Version, group.Id, before));
    }

    private bool TryGroup(string? listId, out Guid group)
    {
        var id = Guid.Empty;
        var found = listId is not null && listId.StartsWith(ListPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(listId[ListPrefix.Length..], "N", out id)
            && Plan.Groups.Any(item => item.Id == id);
        group = id;
        return found;
    }

    // Sortable indexes refer to the rows currently shown, so the destination is translated to the full group order.
    private async Task ReorderAsync(FluentSortableListEventArgs args, string? fromListId)
    {
        if (Busy || !TryGroup(args.ToListId, out var target)) { return; }
        var source = target;
        if (fromListId is not null && !TryGroup(fromListId, out source)) { return; }
        var shown = Rows(source);
        if (args.OldIndex < 0 || args.OldIndex >= shown.Length) { return; }
        var category = shown[args.OldIndex].Category.Id;
        var visible = Rows(target).Select(item => item.Category.Id).Where(id => id != category).ToList();
        var index = Math.Clamp(args.NewIndex, 0, visible.Count);
        var current = Order(target);
        var order = current.Where(id => id != category).ToList();
        Guid? before = null;
        if (index < visible.Count)
        {
            before = visible[index];
        }
        else if (index > 0)
        {
            var next = order.IndexOf(visible[index - 1]) + 1;
            before = next < order.Count ? order[next] : null;
        }
        order.Insert(before is { } id ? order.IndexOf(id) : order.Count, category);
        if (source == target && order.SequenceEqual(current)) { return; }
        var pending = new Dictionary<Guid, Guid[]> { [target] = [.. order] };
        if (source != target)
        {
            pending[source] = Order(source).Where(id => id != category).ToArray();
        }
        _pending = pending;
        try
        {
            await OnCommand.InvokeAsync(new ReorderCategory(Plan.Version, category, target, before));
        }
        finally
        {
            _pending = [];
        }
    }

    private void ClearDrag() => _dragGroup = null;
    private void ClearFocusCategory() => _restoreFocusCategory = null;
    private void DragGroup(Guid id) { ClearDrag(); if (!Busy) { _dragGroup = id; } }
    private Task DropGroupAsync(Guid group) => DropAsync(group);
    private Task DropGroupAtEndAsync() => DropAsync(null);
    private Task DropAsync(Guid? before)
    {
        var command = !Busy && _dragGroup is { } source && source != before ? new ReorderGroup(Plan.Version, source, before) : null;
        ClearDrag();
        return command is null ? Task.CompletedTask : OnCommand.InvokeAsync(command);
    }

    private sealed class TableInterop(IJSRuntime js) : IAsyncDisposable
    {
        private const string ModulePath = "./_content/YHAB.UI/Features/Budgeting/Components/BudgetCategoryTable.razor.js";
        private IJSObjectReference? _module;
        private ElementReference _table;

        public async ValueTask InitializeAsync(ElementReference table, CancellationToken cancellationToken)
        {
            _table = table;
            try
            {
                _module = await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
                await _module.InvokeVoidAsync("initialize", cancellationToken, table);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* The component was disposed before the drag keys were wired up. */ }
            catch (JSDisconnectedException) { /* The circuit closed before the drag keys were wired up. */ }
        }

        public async ValueTask DisposeAsync()
        {
            if (_module is null) { return; }
            try
            {
                // Browser cleanup must run after component cancellation; a disconnected circuit has already released it.
                await _module.InvokeVoidAsync("dispose", CancellationToken.None, _table);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The disconnected circuit has already released the browser resources.
            }
        }
    }
}
