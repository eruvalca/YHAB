using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Plans(IBudgetClient budgets, NavigationManager navigation)
{
    // The installed app's "Add transaction" shortcut enters here, so the intent
    // has one stable URL regardless of how many plans the user can open.
    public const string AddTransactionAction = "add-transaction";
    private const string ConnectionError = "We couldn't reach your plans. Check your connection and refresh.";
    private IReadOnlyList<PlanSummary> _ownedPlans = [];
    private IReadOnlyList<PlanSummary> _sharedPlans = [];
    private string? _error;
    private string? _createError;
    private bool _choosePlanForTransaction;
    private EditContext _joinEditContext = default!;
    private ValidationMessageStore _joinMessages = default!;
    [SupplyParameterFromQuery(Name = "action")] public string? ShortcutAction { get; set; }
    [SupplyParameterFromForm(FormName = "CreatePlan")] private PlanInput Input { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "JoinPlan")] private JoinInput Join { get; set; } = default!;
    private bool IsAddTransaction => string.Equals(ShortcutAction, AddTransactionAction, StringComparison.OrdinalIgnoreCase);

    protected override async Task OnInitializedAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        Input ??= new();
        Join ??= new();
        _joinEditContext = new(Join);
        _joinMessages = new(_joinEditContext);
        try
        {
            var plans = await budgets.ListAsync(token);
            token.ThrowIfCancellationRequested();
            _ownedPlans = plans.Where(item => item.IsOwner).ToArray();
            _sharedPlans = plans.Where(item => !item.IsOwner).ToArray();
        }
        catch (BudgetRequestException exception)
        {
            _error = exception.Message;
        }

        if (!IsDisposed && _error is null && IsAddTransaction)
        {
            var plans = _ownedPlans.Concat(_sharedPlans).ToArray();
            if (plans.Length == 1)
            {
                // The register owns the one-shot intent; this page only resolves the plan.
                navigation.NavigateTo(PlanHref(plans[0].Id));
                return;
            }

            _choosePlanForTransaction = plans.Length > 1;
        }
    }

    private string PlanHref(Guid planId) => IsAddTransaction ? $"/plans/{planId}/accounts?new=true" : $"/plans/{planId}";

    private string PlanActionLabel => IsAddTransaction ? "Add transaction" : "Open plan";

    private async Task CreateAsync()
    {
        _error = null;
        _createError = null;
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            var id = await budgets.CreateAsync(new(Input.Name, Input.StarterCategories), token);
            token.ThrowIfCancellationRequested();
            navigation.NavigateTo($"/plans/{id}");
            return;
        }
        catch (BudgetRequestException exception)
        {
            if (RequiresPageNotice(exception)) { _error = exception.Message; }
            else { _createError = exception.Message; }
        }
        catch (HttpRequestException) { _error = ConnectionError; }
    }

    private async Task JoinAsync()
    {
        _error = null;
        ClearJoinErrors();
        using var operation = CreateOperation();
        var token = operation.Token;
        try
        {
            var id = await budgets.JoinAsync(Join.Code, token);
            token.ThrowIfCancellationRequested();
            navigation.NavigateTo($"/plans/{id}");
        }
        catch (BudgetRequestException exception)
        {
            if (RequiresPageNotice(exception)) { _error = exception.Message; }
            else
            {
                _joinMessages.Add(() => Join.Code, exception.Message);
                _joinEditContext.NotifyFieldChanged(_joinEditContext.Field(nameof(Join.Code)));
            }
        }
        catch (HttpRequestException) { _error = ConnectionError; }
    }

    private void JoinInvalid() => ClearJoinErrors();

    private static bool RequiresPageNotice(BudgetRequestException exception) => exception.Status is 401 or 403 or 409;

    private void ClearJoinErrors()
    {
        _joinMessages.Clear();
        _joinEditContext.NotifyFieldChanged(_joinEditContext.Field(nameof(Join.Code)));
    }

    private sealed class PlanInput
    {
        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
        public bool StarterCategories { get; set; } = true;
    }

    private sealed class JoinInput
    {
        [Required, StringLength(64)] public string Code { get; set; } = string.Empty;
    }
}
