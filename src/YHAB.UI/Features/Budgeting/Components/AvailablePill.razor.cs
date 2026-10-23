using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;
using static YHAB.SharedKernel.Budgeting.BudgetFacts;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class AvailablePill
{
    private static readonly CategoryData _readyToAssign = new(Guid.Empty, Guid.Empty, "Ready to assign", "", int.MinValue, false, null, null);
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public BudgetMonth Budget { get; set; } = default!;
    [Parameter, EditorRequired] public CategoryMonth Row { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter] public string Tone { get; set; } = "neutral";
    [Parameter] public bool Busy { get; set; }
    private CategoryData[] _targets = [];
    private Dictionary<Guid, decimal> _available = [];
    private bool _open;
    private long _draftVersion;
    private bool _draftOverspent;
    private bool _valid = true;
    private bool _saving;
    private Guid? _other;
    private decimal _amount;
    private AmountInput? _amountInput;
    private string AnchorId => $"available-{Row.Category.Id:N}";
    private bool Overspent => Row.Available < 0m;
    private string Heading => Overspent ? "Cover overspending" : "Move money";
    private string SelectLabel => Overspent ? "Cover from" : "Move to";
    private string Description => Overspent
        ? $"{Row.Category.Name} is {Money(Math.Abs(Row.Available))} overspent. Choose where to cover it from."
        : $"Move money out of {Row.Category.Name}. Choose where it goes.";
    private string AriaLabel => Overspent
        ? $"{Row.Category.Name} is {Money(Math.Abs(Row.Available))} overspent. Cover overspending."
        : $"{Money(Row.Available)} available in {Row.Category.Name}. Move money.";
    private string SourceName => _targets.FirstOrDefault(item => item.Id == (_other ?? Guid.Empty))?.Name ?? "Ready to assign";
    private decimal SourceAvailable
    {
        get
        {
            if (!Overspent)
            {
                return Row.Available;
            }

            return _other is { } id ? AvailableIn(id) : Budget.ReadyToAssign;
        }
    }
    private bool CanMove => !Busy && !_saving && _valid && _amount > 0m && _amount <= Math.Max(0m, SourceAvailable);

    protected override void OnParametersSet()
    {
        // Closed rows keep no destination list: building one for every category
        // would make a wide catalog render an option set per row.
        if (!_open)
        {
            return;
        }

        // A saved plan or a month change can make an open draft describe a
        // different transfer. Close it instead of reinterpreting the amount; the
        // user confirms the new operation from the refreshed row.
        if (Plan.Version != _draftVersion || Overspent != _draftOverspent)
        {
            _open = false;
            return;
        }

        _targets = BuildTargets();
        if (!_targets.Any(item => item.Id == (_other ?? Guid.Empty)))
        {
            _open = false;
        }
    }

    private CategoryData[] BuildTargets()
    {
        _available = Budget.Categories.ToDictionary(item => item.Category.Id, item => item.Available);
        var groupOrder = Plan.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Id)
            .Select((item, index) => (item.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        var hiddenGroups = Plan.Groups.Where(item => item.Hidden).Select(item => item.Id).ToHashSet();
        var others = Plan.Categories
            .Where(item => item.Id != Row.Category.Id && !item.Hidden && !hiddenGroups.Contains(item.GroupId))
            .Where(item => !Overspent || AvailableIn(item.Id) > 0m)
            .OrderBy(item => groupOrder.GetValueOrDefault(item.GroupId, int.MaxValue))
            .ThenBy(item => item.SortOrder).ThenBy(item => item.Id)
            .ToArray();

        // A source with no money cannot cover anything, so it is only offered
        // while it can fund the move; Ready to assign is always a valid target.
        return Overspent && Budget.ReadyToAssign <= 0m ? others : [_readyToAssign, .. others];
    }

    private decimal AvailableIn(Guid categoryId) => _available.GetValueOrDefault(categoryId);

    private string LimitText
    {
        get
        {
            if (!Overspent)
            {
                return $"{Money(Row.Available)} available to move.";
            }

            var shortfall = Math.Abs(Row.Available);
            var available = Math.Max(0m, SourceAvailable);
            return available >= shortfall
                ? $"{SourceName} has {Money(available)} available."
                : $"{SourceName} has {Money(available)} available; {Money(shortfall - available)} more is needed to cover it all.";
        }
    }

    private void Toggle()
    {
        if (_open)
        {
            _open = false;
            return;
        }

        ResetDraft();
        _open = true;
    }

    private void ResetDraft()
    {
        _targets = BuildTargets();
        _draftVersion = Plan.Version;
        _draftOverspent = Overspent;
        _other = Overspent && Budget.ReadyToAssign <= 0m ? _targets.FirstOrDefault()?.Id : null;
        _valid = true;
        _amount = Overspent ? Math.Min(Math.Abs(Row.Available), Math.Max(0m, SourceAvailable)) : 0m;
    }

    private void Close() => _open = false;

    private Task OpenedChangedAsync(bool opened)
    {
        _open = opened;
        return Task.CompletedTask;
    }

    private Task TargetChangedAsync(Guid value)
    {
        _other = value == Guid.Empty ? null : value;
        if (Overspent)
        {
            var limit = Math.Max(0m, SourceAvailable);
            var next = _amount <= 0m ? Math.Min(Math.Abs(Row.Available), limit) : Math.Min(_amount, limit);
            if (next != _amount)
            {
                // The source change replaces the draft, so an earlier invalid
                // expression no longer describes what will be saved.
                _amount = next;
                _valid = true;
            }
        }

        return Task.CompletedTask;
    }

    private async Task MoveAsync()
    {
        if (!CanMove || _amountInput is null)
        {
            return;
        }

        _saving = true;
        try
        {
            var amount = await _amountInput.CommitAsync();
            if (amount is not { } value || value <= 0m || value > Math.Max(0m, SourceAvailable))
            {
                return;
            }

            await OnCommand.InvokeAsync(Overspent
                ? new MoveMoney(Plan.Version, _other, Row.Category.Id, Budget.Month, value)
                : new MoveMoney(Plan.Version, Row.Category.Id, _other, Budget.Month, value));
        }
        finally
        {
            _saving = false;
        }
    }
}
