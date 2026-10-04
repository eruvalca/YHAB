using System.Globalization;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Reports(IBudgetClient budgets)
{
    [Parameter] public Guid PlanId { get; set; }
    [SupplyParameterFromQuery] public string? From { get; set; }
    [SupplyParameterFromQuery] public string? To { get; set; }
    private PlanSnapshot? _plan;
    private IReadOnlyList<AccountBalance> _balances = [];
    private IReadOnlyList<MonthReport> _months = [];
    private IReadOnlyList<CategorySpending> _spending = [];
    private string? _error;
    private DateOnly _from;
    private DateOnly _through;
    private decimal MaxSpending => Math.Max(1, _spending.Count == 0 ? 0 : _spending.Max(item => item.Amount));

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var view = await budgets.ReadViewAsync(PlanId);
            _plan = view.Catalog;
            _balances = view.Balances;
            _through = Parse(To, _plan.Today);
            _from = Parse(From, BudgetFacts.Month(_through).AddMonths(-5));
            if (_through < _from || _from.Year < 2000 || _through.Year > 2100 || _through.DayNumber - _from.DayNumber > 732)
            {
                _error = "Choose an ordered date range of up to two years between 2000 and 2100.";
                _from = BudgetFacts.Month(_plan.Today).AddMonths(-5);
                _through = _plan.Today;
            }
            var report = await budgets.ReadReportsAsync(PlanId, _from, _through);
            _months = report.Months;
            _spending = report.Spending;
        }
        catch (BudgetRequestException exception) { _error = exception.Message; }
    }
    private static DateOnly Parse(string? value, DateOnly fallback) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : fallback;
}
