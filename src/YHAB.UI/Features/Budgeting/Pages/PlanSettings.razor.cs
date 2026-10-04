using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class PlanSettings(IBudgetClient budgets)
{
    [Parameter] public Guid PlanId { get; set; }
    [SupplyParameterFromForm(FormName = "PlanSettings")] private SettingsInput Settings { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "RenamePayee")] private PayeeInput Payee { get; set; } = default!;
    private PlanSnapshot? _plan;
    private IReadOnlyList<AccountBalance> _balances = [];
    private IReadOnlyList<string> _payees = [];
    private string? _message;
    private bool _failed;
    protected override async Task OnInitializedAsync()
    {
        try
        {
            await LoadAsync();
            Settings ??= new() { Name = _plan!.Name, Notes = _plan.Notes, Version = _plan.Version };
            Payee ??= new() { Version = _plan!.Version };
        }
        catch (BudgetRequestException exception) { _message = exception.Message; _failed = true; }
    }
    private Task SaveAsync() => ExecuteAsync(new UpdatePlan(Settings.Version, Settings.Name, Settings.Notes), "Plan settings saved.");
    private Task RenameAsync() => ExecuteAsync(new RenamePayee(Payee.Version, Payee.OldName, Payee.NewName), "Payee renamed.");
    private async Task ExecuteAsync(PlanCommand command, string message)
    {
        try
        {
            await budgets.ExecuteAsync(PlanId, command);
            await LoadAsync();
            Settings.Version = _plan!.Version;
            Payee.Version = _plan.Version;
            _message = message;
        }
        catch (BudgetRequestException exception) { _message = exception.Message; _failed = true; }
    }
    private async Task LoadAsync()
    {
        var view = await budgets.ReadViewAsync(PlanId);
        _plan = view.Catalog;
        _balances = view.Balances;
        _payees = await budgets.ReadPayeesAsync(PlanId);
    }
    private sealed class SettingsInput
    {
        public long Version { get; set; }
        [Required, StringLength(100)] public string Name { get; set; } = "";
        [StringLength(4000)] public string Notes { get; set; } = "";
    }
    private sealed class PayeeInput
    {
        public long Version { get; set; }
        [Required, StringLength(200)] public string OldName { get; set; } = "";
        [Required, StringLength(200)] public string NewName { get; set; } = "";
    }
}
