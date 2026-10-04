using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetBoard
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter] public bool Busy { get; set; }
    [Parameter] public Func<DateOnly, Task<BudgetMonth>>? LoadMonth { get; set; }

    private string? _error;
    private int _request;
    private bool IsBusy { get => Busy || field || _error is not null; set; }
    private static readonly string[] _filters = ["All categories", "Underfunded", "Overspent", "Available", "Hidden"];
    private string _filter = "All categories";
    private DateOnly _month;
    private BudgetMonth _budget = default!;
    private long _version = -1;
    private PlanSnapshot? _loadedPlan;
    private bool _move;
    private bool _categoryEditor;
    private bool _groupEditor;
    private CategoryData? _category;
    private CategoryMonth? EditingMonth => _budget.Categories.SingleOrDefault(item => item.Category.Id == _category?.Id);

    protected override async Task OnParametersSetAsync()
    {
        if (_month == default) { _month = BudgetFacts.Month(Plan.Today); }
        if (_loadedPlan?.Id != Plan.Id || _version != Plan.Version || _budget is null || (_error is not null && !ReferenceEquals(_loadedPlan, Plan)))
        {
            CloseEditors();
            _version = Plan.Version;
            _loadedPlan = Plan;
            await CalculateAsync();
        }
    }
    private async Task CalculateAsync()
    {
        var request = ++_request;
        IsBusy = true;
        _error = null;
        try
        {
            var result = LoadMonth is null ? BudgetCalculator.Calculate(Plan, _month, Plan.Today) : await LoadMonth(_month);
            if (request == _request) { _budget = result; }
        }
        catch (BudgetRequestException exception) { if (request == _request) { _error = exception.Message; } }
        catch (HttpRequestException) { if (request == _request) { _error = "Unable to load this month. Refresh your plan to try again."; } }
        finally { if (request == _request) { IsBusy = false; } }
    }
    private async Task PreviousAsync() { _month = _month.AddMonths(-1); await CalculateAsync(); }
    private async Task NextAsync() { _month = _month.AddMonths(1); await CalculateAsync(); }
    private async Task CurrentAsync() { _month = BudgetFacts.Month(Plan.Today); await CalculateAsync(); }
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
