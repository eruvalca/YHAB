using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class PlanSettings(IBudgetClient budgets, NavigationManager navigation)
{
    private const string ConnectionError = "We couldn't reach your plan. Check your connection and refresh.";
    private const string SavedRefreshError = "Your change was saved. Reload to update the displayed settings.";
    [Parameter] public Guid PlanId { get; set; }
    [SupplyParameterFromForm(FormName = "PlanSettings")] private SettingsInput Settings { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "RenamePayee")] private PayeeInput Payee { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "CreateShareCode")] private ShareCodeInput ShareCodeForm { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "RemoveShare")] private RemoveInput Remove { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "LeavePlan")] private LeaveInput Leave { get; set; } = default!;
    private PlanSnapshot? _plan;
    private PlanShareSummary? _sharing;
    private PlanShareCode? _shareCode;
    private IReadOnlyList<AccountBalance> _balances = [];
    private IReadOnlyList<string> _payees = [];
    private string? _pageError;
    private Feedback? _settingsFeedback;
    private Feedback? _renameFeedback;
    private Feedback? _shareFeedback;
    private Feedback? _removeFeedback;
    private Feedback? _leaveFeedback;
    protected override async Task OnInitializedAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            await LoadAsync(token);
            Settings ??= new() { Name = _plan!.Name, Notes = _plan.Notes, Version = _plan.Version };
            Payee ??= new() { Version = _plan!.Version };
            ShareCodeForm ??= new();
            Remove ??= new();
            Leave ??= new();
        }
        catch (BudgetRequestException exception) { _pageError = exception.Message; }
    }
    private Task SaveAsync()
    {
        _pageError = null;
        _settingsFeedback = null;
        return ExecuteAsync(new UpdatePlan(Settings.Version, Settings.Name, Settings.Notes), "Plan settings saved.", feedback => _settingsFeedback = feedback);
    }
    private Task RenameAsync()
    {
        _pageError = null;
        _renameFeedback = null;
        return ExecuteAsync(new RenamePayee(Payee.Version, Payee.OldName, Payee.NewName), "Payee renamed.", feedback => _renameFeedback = feedback);
    }
    private void SaveInvalid() => _settingsFeedback = null;
    private void RenameInvalid() => _renameFeedback = null;
    private async Task CreateShareCodeAsync()
    {
        _pageError = null;
        _shareFeedback = null;
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            _shareCode = await budgets.CreateShareCodeAsync(PlanId, token);
            token.ThrowIfCancellationRequested();
            _shareFeedback = new("Share code created. Give it to the person you want to add.", false);
        }
        catch (BudgetRequestException exception) { ShowActionError(exception, feedback => _shareFeedback = feedback); }
        catch (HttpRequestException) { _pageError = ConnectionError; }
    }
    private async Task RemoveAsync()
    {
        _pageError = null;
        _removeFeedback = null;
        if (string.IsNullOrWhiteSpace(Remove.MemberId))
        {
            _removeFeedback = new("Choose a person to remove.", true);
            return;
        }
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            _sharing = await budgets.UnshareAsync(PlanId, Remove.MemberId, token);
            token.ThrowIfCancellationRequested();
            Remove = new();
            _removeFeedback = new("That account can no longer open this plan.", false);
        }
        catch (BudgetRequestException exception) { ShowActionError(exception, feedback => _removeFeedback = feedback); }
        catch (HttpRequestException) { _pageError = ConnectionError; }
    }
    private void LeaveInvalid() => _leaveFeedback = null;
    private async Task LeaveAsync()
    {
        _pageError = null;
        _leaveFeedback = null;
        if (!Leave.Confirmed)
        {
            _leaveFeedback = new("Confirm that you want to leave this plan.", true);
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
        catch (BudgetRequestException exception) { ShowActionError(exception, feedback => _leaveFeedback = feedback); }
        catch (HttpRequestException) { _pageError = ConnectionError; }
    }
    private async Task ExecuteAsync(PlanCommand command, string message, Action<Feedback?> setFeedback)
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
            setFeedback(new(message, false));
        }
        catch (BudgetRequestException exception)
        {
            setFeedback(null);
            if (RequiresPageNotice(exception) || saved)
            {
                _pageError = saved ? SavedRefreshError : exception.Message;
            }
            else
            {
                setFeedback(new(exception.Message, true));
            }
        }
        catch (HttpRequestException) { _pageError = saved ? SavedRefreshError : ConnectionError; }
    }

    private void ShowActionError(BudgetRequestException exception, Action<Feedback?> setFeedback)
    {
        if (RequiresPageNotice(exception))
        {
            setFeedback(null);
            _pageError = exception.Message;
        }
        else
        {
            setFeedback(new(exception.Message, true));
        }
    }

    private static bool RequiresPageNotice(BudgetRequestException exception) => exception.Status is 401 or 403 or 409;

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
    private sealed record Feedback(string Message, bool IsError);
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
    private sealed class ShareCodeInput
    {
        public string Action { get; set; } = "create";
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
