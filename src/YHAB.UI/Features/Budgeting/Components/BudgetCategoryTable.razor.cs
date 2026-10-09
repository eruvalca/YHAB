using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetCategoryTable
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public BudgetMonth Budget { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback<CategoryData> OnSelect { get; set; }
    [Parameter] public Guid? SelectedCategoryId { get; set; }
    [Parameter] public bool Busy { get; set; }
    private static readonly string[] _filters = ["All categories", "Underfunded", "Overspent", "Available", "Hidden"];
    private string _filter = "All categories";
    private readonly HashSet<Guid> _collapsed = [];
    private Guid? _dragCategory;
    private Guid? _dragGroup;
    private Guid? _movingGroup;
    private Guid? _newCategoryGroup;
    private bool _newGroup;
    private long _version = -1;
    private GroupData[] Groups { get; set; } = [];

    protected override void OnParametersSet()
    {
        Groups = Plan.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray();
        if (_version != Plan.Version)
        {
            _version = Plan.Version;
            CancelCreate();
            ClearDrag();
        }
    }

    private CategoryMonth[] Rows(Guid group) => Budget.Categories.Where(item => item.Category.GroupId == group
        && (string.Equals(_filter, "Hidden", StringComparison.Ordinal) ? item.Category.Hidden || Plan.Groups.Any(value => value.Id == group && value.Hidden) : !item.Category.Hidden && !Plan.Groups.Any(value => value.Id == group && value.Hidden))
        && (_filter switch { "Underfunded" => item.TargetNeeded > 0, "Overspent" => item.Available < 0, "Available" => item.Available > 0, _ => true }))
        .OrderBy(item => item.Category.SortOrder).ThenBy(item => item.Category.Id).ToArray();
    private bool ShowGroup(GroupData group, int count) => count > 0 || _newCategoryGroup == group.Id
        || (string.Equals(_filter, "All categories", StringComparison.Ordinal) && !group.Hidden)
        || (string.Equals(_filter, "Hidden", StringComparison.Ordinal) && group.Hidden);
    private void ToggleGroup(Guid id) { if (!_collapsed.Add(id)) { _collapsed.Remove(id); } }
    private void ToggleAll() { if (_collapsed.Count > 0) { _collapsed.Clear(); } else { _collapsed.UnionWith(Plan.Groups.Select(item => item.Id)); } }
    private void ToggleGroupMove(Guid id) => _movingGroup = _movingGroup == id ? null : id;
    private void AddGroup() { CancelCreate(); _filter = "All categories"; _newGroup = true; }
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
    private void ClearDrag() { _dragCategory = null; _dragGroup = null; }
    private void DragCategory(Guid? id) { ClearDrag(); if (!Busy) { _dragCategory = id; } }
    private void DragGroup(Guid id) { ClearDrag(); if (!Busy) { _dragGroup = id; } }
    private Task DropCategoryAsync(CategoryData before) => DropAsync(before.GroupId, before.Id);
    private Task DropGroupAsync(Guid group) => DropAsync(group, null);
    private Task DropGroupAtEndAsync() => DropAsync(null, null);
    private Task DropAsync(Guid? group, Guid? before)
    {
        PlanCommand? command = null;
        if (!Busy && _dragCategory is { } category && group.HasValue && category != before)
        {
            command = new ReorderCategory(Plan.Version, category, group.Value, before);
        }
        else if (!Busy && _dragGroup is { } source && source != group)
        {
            command = new ReorderGroup(Plan.Version, source, group);
        }
        ClearDrag();
        return command is null ? Task.CompletedTask : OnCommand.InvokeAsync(command);
    }
}
