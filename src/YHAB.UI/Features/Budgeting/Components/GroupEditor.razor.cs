using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;
namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class GroupEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter] public bool Busy { get; set; }
    private GroupData[] _groups = [];
    private Guid _id;
    private string _name = "";
    private int _order;
    private bool _hidden;
    protected override void OnInitialized() { _groups = [new(Guid.Empty, "New group", 0), .. Plan.Groups]; _order = Plan.Groups.Count; }
    private void SelectGroup(Guid id) { _id = id; var group = Plan.Groups.SingleOrDefault(item => item.Id == id); _name = group?.Name ?? ""; _order = group?.SortOrder ?? Plan.Groups.Count; _hidden = group?.Hidden ?? false; }
    private Task SaveAsync() => OnCommand.InvokeAsync(new SaveGroup(Plan.Version, new(_id, _name, _order, _hidden)));
}

