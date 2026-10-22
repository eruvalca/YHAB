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
    protected override void OnInitialized()
    {
        _categories = [new(Guid.Empty, Guid.Empty, "Ready to assign", "", 0, false, null, null), .. Plan.Categories];
        _to = Plan.Categories.Count > 0 ? Plan.Categories[0].Id : Guid.Empty;
    }
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
