using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetCategoryRow
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public CategoryMonth Row { get; set; } = default!;
    [Parameter, EditorRequired] public DateOnly Month { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback<CategoryData> OnSelect { get; set; }
    [Parameter, EditorRequired] public EventCallback<Guid?> OnDrag { get; set; }
    [Parameter, EditorRequired] public EventCallback<CategoryData> OnDrop { get; set; }
    [Parameter] public bool Busy { get; set; }
    [Parameter] public bool Selected { get; set; }
    private bool _moving;
    private CategoryData[] Siblings { get; set; } = [];
    protected override void OnParametersSet() => Siblings = Plan.Categories.Where(item => item.GroupId == Row.Category.GroupId).OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray();
    private int Index => Array.FindIndex(Siblings, item => item.Id == Row.Category.Id);
    private string Tone => Row switch { { Available: < 0 } => "negative", { TargetNeeded: > 0 } => "attention", { Available: > 0 } => "positive", _ => "neutral" };
    private string TargetCaption => Row switch { { Snoozed: true } => "Target snoozed this month", { TargetNeeded: > 0 } => $"{BudgetFacts.Money(Row.TargetNeeded)} more needed", _ => "Target funded" };
    private decimal Progress => Row.TargetTotal <= 0 ? 0 : Math.Clamp(100 * (1 - Row.TargetNeeded / Row.TargetTotal), 0, 100);
    private Task SelectAsync() => Busy ? Task.CompletedTask : OnSelect.InvokeAsync(Row.Category);
    private Task RenameAsync(string name) => OnCommand.InvokeAsync(new SaveCategory(Plan.Version, Row.Category with { Name = name }));
    private Task AssignAsync(decimal amount) => OnCommand.InvokeAsync(new AssignMoney(Plan.Version, Row.Category.Id, Month, amount));
    private void ToggleMove() => _moving = !_moving;
    private Task DragAsync() => OnDrag.InvokeAsync(Row.Category.Id);
    private Task EndDragAsync() => OnDrag.InvokeAsync(null);
    private Task DropAsync() => OnDrop.InvokeAsync(Row.Category);
    private Task MoveUpAsync() => MoveAsync(Row.Category.GroupId, Siblings[Index - 1].Id);
    private Task MoveDownAsync() => MoveAsync(Row.Category.GroupId, Siblings.ElementAtOrDefault(Index + 2)?.Id);
    private Task MoveToGroupAsync(Guid group) => group == Row.Category.GroupId ? Task.CompletedTask : MoveAsync(group, null);
    private Task MoveAsync(Guid group, Guid? before) => Busy ? Task.CompletedTask : OnCommand.InvokeAsync(new ReorderCategory(Plan.Version, Row.Category.Id, group, before));
}
