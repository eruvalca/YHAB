using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class PlanSettings(IBudgetClient budgets, NavigationManager navigation)
{
    [Parameter] public Guid PlanId { get; set; }
    [SupplyParameterFromForm(FormName = "PlanSettings")] private SettingsInput Settings { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "RenamePayee")] private PayeeInput Payee { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "SharePlan")] private ShareInput Share { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "RemoveShare")] private RemoveInput Remove { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "LeavePlan")] private LeaveInput Leave { get; set; } = default!;
    private PlanSnapshot? _plan;
    private PlanShareSummary? _sharing;
    private IReadOnlyList<AccountBalance> _balances = [];
    private IReadOnlyList<string> _payees = [];
    private string? _message;
    private bool _failed;
    protected override async Task OnInitializedAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            await LoadAsync(token);
            Settings ??= new() { Name = _plan!.Name, Notes = _plan.Notes, Version = _plan.Version };
            Payee ??= new() { Version = _plan!.Version };
            Share ??= new();
            Remove ??= new();
            Leave ??= new();
        }
        catch (BudgetRequestException exception) { _message = exception.Message; _failed = true; }
    }
    private Task SaveAsync() => ExecuteAsync(new UpdatePlan(Settings.Version, Settings.Name, Settings.Notes), "Plan settings saved.");
    private Task RenameAsync() => ExecuteAsync(new RenamePayee(Payee.Version, Payee.OldName, Payee.NewName), "Payee renamed.");
    private async Task ShareAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            _sharing = await budgets.ShareAsync(PlanId, Share.Email, token);
            token.ThrowIfCancellationRequested();
            Share = new();
            _message = "Plan shared. That account can now open and edit it.";
            _failed = false;
        }
        catch (BudgetRequestException exception) { _message = exception.Message; _failed = true; }
    }
    private async Task RemoveAsync()
    {
        if (string.IsNullOrWhiteSpace(Remove.MemberId))
        {
            _message = "Choose a person to remove.";
            _failed = true;
            return;
        }
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            _sharing = await budgets.UnshareAsync(PlanId, Remove.MemberId, token);
            token.ThrowIfCancellationRequested();
            Remove = new();
            _message = "That account can no longer open this plan.";
            _failed = false;
        }
        catch (BudgetRequestException exception) { _message = exception.Message; _failed = true; }
    }
    private async Task LeaveAsync()
    {
        if (!Leave.Confirmed)
        {
            _message = "Confirm that you want to leave this plan.";
            _failed = true;
            return;
        }
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            await budgets.LeaveAsync(PlanId, token);
            token.ThrowIfCancellationRequested();
            navigation.NavigateTo("/plans");
        }
        catch (BudgetRequestException exception) { _message = exception.Message; _failed = true; }
    }
    private async Task ExecuteAsync(PlanCommand command, string message)
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        var saved = false;
        try
        {
            await budgets.ExecuteAsync(PlanId, command, token);
            saved = true;
            await LoadAsync(token);
            Settings.Version = _plan!.Version;
            Payee.Version = _plan.Version;
            _message = message;
        }
        catch (BudgetRequestException exception)
        {
            _message = saved ? "Your change was saved. Reload to update the displayed settings." : exception.Message;
            _failed = true;
        }
    }
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var view = await budgets.ReadViewAsync(PlanId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var payees = await budgets.ReadPayeesAsync(PlanId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var sharing = await budgets.ReadSharingAsync(PlanId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _plan = view.Catalog;
        _balances = view.Balances;
        _payees = payees;
        _sharing = sharing;
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
    private sealed class ShareInput
    {
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    }
    private sealed class RemoveInput
    {
        [Required] public string MemberId { get; set; } = "";
    }
    private sealed class LeaveInput
    {
        [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that you want to leave this plan.")]
        public bool Confirmed { get; set; }
    }
}
