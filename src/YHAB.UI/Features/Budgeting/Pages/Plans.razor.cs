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
        Input ??= new();
        try
        {
            _plans = await budgets.ListAsync();
        }
        catch (BudgetRequestException exception)
        {
            _error = exception.Message;
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            var id = await budgets.CreateAsync(new(Input.Name, Input.StarterCategories));
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
