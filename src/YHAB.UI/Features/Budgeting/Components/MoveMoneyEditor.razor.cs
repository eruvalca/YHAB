using System.Globalization;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class MoveMoneyEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public DateOnly Month { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter] public bool Busy { get; set; }
    private CategoryData[] _categories = [];
    private Guid _from;
    private Guid _to;
    private decimal _amount;
    private bool _valid = true;
    private bool _saving;
    private AmountInput? _amountInput;
    private string _initial = "";
    private bool HasUnsavedChanges() => !string.Equals(Signature(), _initial, StringComparison.Ordinal) || _amountInput?.HasDraft is true;

    protected override void OnInitialized()
    {
        _categories = BuildCategories();
        _to = Plan.Categories.Count > 0 ? Plan.Categories[0].Id : Guid.Empty;
        _initial = Signature();
    }

    // A refresh can replace the catalog under an open draft. The chosen categories are kept as they
    // are: one the refresh removed stays visibly unavailable until the user picks a replacement,
    // rather than silently changing the allocation the draft would move.
    protected override void OnParametersSet()
    {
        _categories = BuildCategories();
    }

    private bool FromAvailable => _categories.Any(item => item.Id == _from);
    private bool ToAvailable => _categories.Any(item => item.Id == _to);

    private CategoryData[] BuildCategories() => [new(Guid.Empty, Guid.Empty, "Ready to assign", "", 0, false, null, null), .. Plan.Categories];
    private string Signature() => string.Join("|", _from.ToString(), _to.ToString(), _amount.ToString(CultureInfo.InvariantCulture));
    private async Task SaveAsync()
    {
        if (_saving)
        {
            return;
        }

        _saving = true;
        try
        {
            var amount = await _amountInput!.CommitAsync();
            if (amount is null)
            {
                return;
            }

            _amount = amount.Value;
            await OnCommand.InvokeAsync(new MoveMoney(Plan.Version, _from == Guid.Empty ? null : _from, _to == Guid.Empty ? null : _to, Month, _amount));
        }
        finally
        {
            _saving = false;
        }
    }
}
