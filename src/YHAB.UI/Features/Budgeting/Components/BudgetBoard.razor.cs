using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetBoard
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter] public bool Busy { get; set; }
    private static readonly string[] _filters = ["All categories", "Underfunded", "Overspent", "Available", "Hidden"];
    private string _filter = "All categories";
    private DateOnly _month;
    private BudgetMonth _budget = default!;
    private long _version = -1;
    private bool _move;
    private bool _categoryEditor;
    private bool _groupEditor;
    private CategoryData? _category;
    private string ReadyTitle => _budget.ReadyToAssign switch { < 0 => "Bring your plan back into balance.", > 0 => "What matters to you this month?", _ => "Every dollar has a purpose." };
    private string ReadyDescription => _budget.ReadyToAssign switch { < 0 => "Reduce assignments or move money back to Ready to assign.", > 0 => "Give this money a job, from everyday essentials to your next big thing.", _ => "Your plan is ready. Adjust it as life happens." };

    protected override void OnParametersSet()
    {
        if (_month == default) { _month = BudgetFacts.Month(Plan.Today); }
        if (_version != Plan.Version) { CloseEditors(); _version = Plan.Version; }
        Calculate();
    }
    private void Calculate() => _budget = BudgetCalculator.Calculate(Plan, _month, Plan.Today);
    private void Previous() { _month = _month.AddMonths(-1); Calculate(); }
    private void Next() { _month = _month.AddMonths(1); Calculate(); }
    private void Current() { _month = BudgetFacts.Month(Plan.Today); Calculate(); }
    private IEnumerable<CategoryMonth> Rows(Guid group) => _budget.Categories.Where(item => item.Category.GroupId == group
        && (string.Equals(_filter, "Hidden", StringComparison.Ordinal) ? item.Category.Hidden || Plan.Groups.Any(value => value.Id == group && value.Hidden) : !item.Category.Hidden && !Plan.Groups.Any(value => value.Id == group && value.Hidden))
        && (_filter switch { "Underfunded" => item.TargetNeeded > 0, "Overspent" => item.Available < 0, "Available" => item.Available > 0, _ => true }))
        .OrderBy(item => item.Category.SortOrder);
    private static string Tone(CategoryMonth row) => row switch { { Available: < 0 } => "negative", { TargetNeeded: > 0 } => "attention", { Available: > 0 } => "positive", _ => "neutral" };
    private static string TargetCaption(CategoryMonth row) => row switch { { Snoozed: true } => "Target snoozed this month", { TargetNeeded: > 0 } => $"{BudgetFacts.Money(row.TargetNeeded)} more needed", _ => "Target funded" };
    private static decimal Progress(CategoryMonth row) => row.TargetTotal <= 0 ? 0 : Math.Clamp(100 * (1 - row.TargetNeeded / row.TargetTotal), 0, 100);
    private Task AssignAsync(Guid category, decimal amount) => OnCommand.InvokeAsync(new AssignMoney(Plan.Version, category, _month, amount));
    private Task AutoAssignAsync() => OnCommand.InvokeAsync(new AutoAssign(Plan.Version, _month));
    private void ShowMove() { CloseEditors(); _move = true; }
    private void AddCategory() { CloseEditors(); _category = null; _categoryEditor = true; }
    private void EditCategory(CategoryData category) { CloseEditors(); _category = category; _categoryEditor = true; }
    private void AddGroup() { CloseEditors(); _groupEditor = true; }
    private void CloseEditors() { _move = false; _categoryEditor = false; _groupEditor = false; }
}
