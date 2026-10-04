using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Plans(IBudgetClient budgets, NavigationManager navigation)
{
    private IReadOnlyList<PlanSummary> _plans = [];
    private string? _error;
    [SupplyParameterFromForm(FormName = "CreatePlan")] private PlanInput Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        Input ??= new();
        try
        {
            var plans = await budgets.ListAsync(token);
            token.ThrowIfCancellationRequested();
            _plans = plans;
        }
        catch (BudgetRequestException exception)
        {
            _error = exception.Message;
        }
    }

    private async Task CreateAsync()
    {
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
            _error = exception.Message;
        }
    }

    private sealed class PlanInput
    {
        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
        public bool StarterCategories { get; set; } = true;
    }
}
