using System.Globalization;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class CategoryEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public CategoryData? Category { get; set; }
    [Parameter] public CategoryMonth? MonthState { get; set; }
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
    private bool _saving;
    private bool _snoozed;
    private decimal _amount;
    private AmountInput? _amountInput;
    private TargetKind _kind;
    private TargetCadence _cadence = TargetCadence.Monthly;
    private DateOnly _due;
    private DateOnly _start;
    private int _repeat;
    private DayOfWeek _weekday = DayOfWeek.Friday;
    private Guid _replacement;
    private string _initial = "";
    private string TargetDescription => BudgetGlossary.Definition(BudgetGlossary.For(_kind));
    private bool HasUnsavedChanges() => !string.Equals(Signature(), _initial, StringComparison.Ordinal) || _amountInput?.HasDraft is true;
    protected override void OnInitialized()
    {
        _name = Category?.Name ?? "";
        _notes = Category?.Notes ?? "";
        _group = Category?.GroupId ?? DefaultGroup;
        _order = Category?.SortOrder ?? Plan.Categories.Count;
        _hidden = Category?.Hidden ?? false;
        InitializeTarget();
        _initial = Signature();
    }
    private string Signature()
    {
        string[] parts =
        [
            _name,
            _notes,
            _group.ToString(),
            _hidden.ToString(),
            _hasTarget.ToString(),
            _kind.ToString(),
            _cadence.ToString(),
            _amount.ToString(CultureInfo.InvariantCulture),
            _due.ToString("O", CultureInfo.InvariantCulture),
            _repeat.ToString(CultureInfo.InvariantCulture),
            _weekday.ToString(),
        ];
        return string.Join("|", parts);
    }
    private Guid DefaultGroup => Plan.Groups.Count > 0 ? Plan.Groups[0].Id : Guid.Empty;
    protected override void OnParametersSet()
    {
        _snoozed = MonthState?.Snoozed ?? false;
        _replacements = [new(Guid.Empty, Guid.Empty, "No history to move", "", 0, false, null, null),
            .. Plan.Categories.Where(item => item.Id != Category?.Id && item.CreditAccountId is null)];
        if (!_replacements.Any(item => item.Id == _replacement)) { _replacement = Guid.Empty; }
    }
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
    private static string KindLabel(TargetKind kind) => BudgetGlossary.Label(BudgetGlossary.For(kind));
    private async Task SaveAsync()
    {
        if (_saving)
        {
            return;
        }

        _saving = true;
        try
        {
            if (_hasTarget)
            {
                var amount = await _amountInput!.CommitAsync();
                if (amount is null)
                {
                    return;
                }

                _amount = amount.Value;
            }

            var due = _cadence is TargetCadence.Custom or TargetCadence.Yearly ? _due : (DateOnly?)null;
            TargetData? target = _hasTarget ? new(_kind, _cadence, _amount, _start, due, _repeat, _weekday) : null;
            await OnCommand.InvokeAsync(new SaveCategory(Plan.Version, new(Category?.Id ?? Guid.Empty, _group, _name, _notes, _order, _hidden, Category?.CreditAccountId, target)));
        }
        finally
        {
            _saving = false;
        }
    }
    private Task SnoozeAsync() => OnCommand.InvokeAsync(new AssignMoney(Plan.Version, Category!.Id, Month,
        MonthState?.Assigned ?? 0, !_snoozed));
    private Task RemoveAsync() => OnCommand.InvokeAsync(new RemoveCategory(Plan.Version, Category!.Id, _replacement == Guid.Empty ? null : _replacement));
}
