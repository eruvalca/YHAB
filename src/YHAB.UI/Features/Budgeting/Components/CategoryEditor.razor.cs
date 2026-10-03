using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;
namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class CategoryEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public CategoryData? Category { get; set; }
    [Parameter, EditorRequired] public DateOnly Month { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter] public bool Busy { get; set; }
    private static readonly TargetKind[] _kinds = Enum.GetValues<TargetKind>();
    private static readonly TargetCadence[] _cadences = Enum.GetValues<TargetCadence>();
    private static readonly DayOfWeek[] _weekdays = Enum.GetValues<DayOfWeek>();
    private CategoryData[] _replacements = [];
    private string _name = "";
    private string _notes = "";
    private Guid _group;
    private int _order;
    private bool _hidden;
    private bool _hasTarget;
    private bool _valid = true;
    private bool _snoozed;
    private decimal _amount;
    private TargetKind _kind;
    private TargetCadence _cadence = TargetCadence.Monthly;
    private DateOnly _due;
    private DateOnly _start;
    private int _repeat;
    private DayOfWeek _weekday = DayOfWeek.Friday;
    private Guid _replacement;
    private string TargetDescription => _kind switch
    {
        TargetKind.Refill => "Refill up to this amount. Money left from the previous period reduces what you need to add.",
        TargetKind.SetAside => "Set aside new money each period, even when money is left over.",
        _ => "Build toward a balance. Spending from this category increases what you need to replace.",
    };
    protected override void OnInitialized()
    {
        _name = Category?.Name ?? "";
        _notes = Category?.Notes ?? "";
        _group = Category?.GroupId ?? DefaultGroup;
        _order = Category?.SortOrder ?? Plan.Categories.Count;
        _hidden = Category?.Hidden ?? false;
        InitializeTarget();
        _snoozed = Plan.Allocations.Any(item => item.CategoryId == Category?.Id && item.Month == Month && item.Snoozed);
        _replacements = [new(Guid.Empty, Guid.Empty, "No history to move", "", 0, false, null, null),
            .. Plan.Categories.Where(item => item.Id != Category?.Id && item.CreditAccountId is null)];
    }
    private Guid DefaultGroup => Plan.Groups.Count > 0 ? Plan.Groups[0].Id : Guid.Empty;
    private void InitializeTarget()
    {
        var target = Category?.Target;
        _hasTarget = target is not null;
        _amount = target?.Amount ?? 0;
        _kind = target?.Kind ?? TargetKind.Refill;
        _cadence = target?.Cadence ?? TargetCadence.Monthly;
        _due = target?.DueDate ?? Month.AddMonths(1).AddDays(-1);
        _start = target?.StartMonth ?? Month;
        _repeat = target?.RepeatEveryMonths ?? 0;
        _weekday = target?.Weekday ?? DayOfWeek.Friday;
    }
    private static string KindLabel(TargetKind kind) => kind switch { TargetKind.Refill => "Refill up to", TargetKind.SetAside => "Set aside another", _ => "Have a balance of" };
    private Task SaveAsync()
    {
        var due = _cadence is TargetCadence.Custom or TargetCadence.Yearly ? _due : (DateOnly?)null;
        TargetData? target = _hasTarget ? new(_kind, _cadence, _amount, _start, due, _repeat, _weekday) : null;
        return OnCommand.InvokeAsync(new SaveCategory(Plan.Version, new(Category?.Id ?? Guid.Empty, _group, _name, _notes, _order, _hidden, Category?.CreditAccountId, target)));
    }
    private Task SnoozeAsync() => OnCommand.InvokeAsync(new AssignMoney(Plan.Version, Category!.Id, Month,
        Plan.Allocations.SingleOrDefault(item => item.CategoryId == Category.Id && item.Month == Month)?.Amount ?? 0, !_snoozed));
    private Task RemoveAsync() => OnCommand.InvokeAsync(new RemoveCategory(Plan.Version, Category!.Id, _replacement == Guid.Empty ? null : _replacement));
}
