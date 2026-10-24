using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Plans(IBudgetClient budgets, NavigationManager navigation)
{
    private const string ConnectionError = "We couldn't reach your plans. Check your connection and refresh.";
    private IReadOnlyList<PlanSummary> _ownedPlans = [];
    private IReadOnlyList<PlanSummary> _sharedPlans = [];
    private string? _error;
    private string? _createError;
    private EditContext _joinEditContext = default!;
    private ValidationMessageStore _joinMessages = default!;
    [SupplyParameterFromForm(FormName = "CreatePlan")] private PlanInput Input { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "JoinPlan")] private JoinInput Join { get; set; } = default!;

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
    }

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
