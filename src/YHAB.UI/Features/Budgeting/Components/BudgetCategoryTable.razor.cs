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
    [Parameter, EditorRequired] public EventCallback<CategoryData> OnSelect { get; set; }
    [Parameter] public Guid? SelectedCategoryId { get; set; }
    [Parameter] public bool Busy { get; set; }
    private static readonly string[] _filters = ["All categories", "Underfunded", "Overspent", "Available", "Hidden"];
    private string _filter = "All categories";
    private readonly HashSet<Guid> _collapsed = [];
    private readonly Dictionary<Guid, InlineNameDraft> _drafts = [];
    private Dictionary<Guid, CategoryMonth> _rows = [];
    // Shows a dropped order while its save is in flight; the confirmed plan replaces it.
    private Dictionary<Guid, Guid[]> _pending = [];
    private Guid? _dragGroup;
    private Guid? _movingGroup;
    private Guid? _movingCategory;
    private Guid? _newCategoryGroup;
    private bool _newGroup;
    private long _version = -1;
    private ElementReference _table;
    private TableInterop? _interop;
    private readonly CancellationTokenSource _disposal = new();
    private GroupData[] Groups { get; set; } = [];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _interop = new(JS);
            await _interop.InitializeAsync(_table, _disposal.Token);
        }
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
                && (_filter switch { "Underfunded" => item.TargetNeeded > 0, "Overspent" => item.Available < 0, "Available" => item.Available > 0, _ => true }))
            .ToArray();
    }

    private bool ShowGroup(GroupData group, int count) => count > 0 || _newCategoryGroup == group.Id
        || (string.Equals(_filter, "All categories", StringComparison.Ordinal) && !group.Hidden)
        || (string.Equals(_filter, "Hidden", StringComparison.Ordinal) && group.Hidden);
    private void ToggleGroup(Guid id) { if (!_collapsed.Add(id)) { _collapsed.Remove(id); } }
    private void ToggleAll() { if (_collapsed.Count > 0) { _collapsed.Clear(); } else { _collapsed.UnionWith(Plan.Groups.Select(item => item.Id)); } }
    private void ToggleGroupMove(Guid id) => _movingGroup = _movingGroup == id ? null : id;
    private void ToggleCategoryMove(Guid id) => _movingCategory = _movingCategory == id ? null : id;
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
            _module = await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
            await _module.InvokeVoidAsync("initialize", cancellationToken, table);
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
