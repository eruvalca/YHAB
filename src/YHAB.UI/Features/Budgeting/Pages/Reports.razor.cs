using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;
using static YHAB.SharedKernel.Budgeting.BudgetFacts;

namespace YHAB.UI.Features.Budgeting.Pages;

public sealed partial class Reports(IBudgetClient budgets)
{
    [Parameter] public Guid PlanId { get; set; }
    [SupplyParameterFromQuery] public string? From { get; set; }
    [SupplyParameterFromQuery] public string? To { get; set; }
    [SupplyParameterFromQuery] public string? Preset { get; set; }
    private PlanSnapshot? _plan;
    private IReadOnlyList<AccountBalance> _balances = [];
    private IReadOnlyList<MonthReport> _months = [];
    private IReadOnlyList<MonthReport> _previousMonths = [];
    private IReadOnlyList<CategorySpending> _spending = [];
    private string? _error;
    private string? _rangeError;
    private DateOnly _from;
    private DateOnly _through;
    private string _fromText = "";
    private string _throughText = "";
    private bool HasActivity => _months.Any(item => item.Income != 0 || item.Expense != 0) || _spending.Count > 0;
    private decimal TotalSpending => _spending.Sum(item => item.Amount);
    private decimal MaxSpending => Math.Max(1, _spending.Count == 0 ? 0 : _spending.Max(item => item.Amount));
    private decimal MaxCashFlow => Math.Max(1, _months.Count == 0 ? 0 : _months.Max(item => Math.Max(Math.Abs(item.Income), Math.Abs(item.Expense))));
    private decimal Income => _months.Sum(item => item.Income);
    private decimal Spending => _months.Sum(item => item.Expense);
    private decimal NetWorth => _months.Count > 0 ? _months[^1].NetWorth : 0;
    private string NetWorthPoints => string.Join(' ', GetChartPoints().Select(item => $"{item.X},{item.Y}"));
    private string CsvFileName => $"reports-{_from:yyyy-MM-dd}-{_through:yyyy-MM-dd}.csv";
    private string CsvHref => "data:text/csv;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildCsv()));
    private ChartPoint[] GetChartPoints()
    {
        if (_months.Count == 0) { return []; }
        var values = _months.Select(item => item.NetWorth).ToArray();
        var minimum = values.Min();
        var maximum = values.Max();
        return _months.Select((month, index) =>
        {
            var x = _months.Count == 1 ? 320d : 24d + index * 592d / (_months.Count - 1);
            var y = minimum == maximum ? 90d : 156d - (double)((month.NetWorth - minimum) / (maximum - minimum)) * 132d;
            return new ChartPoint(FormatCoordinate(x), FormatCoordinate(y), month.Month, month.NetWorth);
        }).ToArray();
    }

    protected override async Task OnParametersSetAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        _error = null;
        _rangeError = null;
        _months = [];
        _previousMonths = [];
        _spending = [];
        try
        {
            var view = await budgets.ReadViewAsync(PlanId, token);
            token.ThrowIfCancellationRequested();
            _plan = view.Catalog;
            _balances = view.Balances;
            if (!TryResolveRange()) { return; }
            var report = await budgets.ReadReportsAsync(PlanId, _from, _through, token);
            token.ThrowIfCancellationRequested();
            _months = report.Months;
            _spending = report.Spending;
            var previousThrough = _from.AddDays(-1);
            var previousFrom = previousThrough.AddDays(-(_through.DayNumber - _from.DayNumber));
            if (previousFrom.Year >= 2000)
            {
                var previous = await budgets.ReadReportsAsync(PlanId, previousFrom, previousThrough, token);
                token.ThrowIfCancellationRequested();
                _previousMonths = previous.Months;
            }
        }
        catch (BudgetRequestException exception) { _error = exception.Message; }
    }

    private bool TryResolveRange()
    {
        var today = _plan!.Today;
        if (Preset is "this-month" or "last-month" or "year-to-date")
        {
            (_from, _through) = Preset switch
            {
                "this-month" => (BudgetFacts.Month(today), today),
                "last-month" => (BudgetFacts.Month(today).AddMonths(-1), BudgetFacts.Month(today).AddDays(-1)),
                _ => (new DateOnly(today.Year, 1, 1), today),
            };
            _fromText = FormatDate(_from);
            _throughText = FormatDate(_through);
        }
        else
        {
            _fromText = From ?? FormatDate(BudgetFacts.Month(today).AddMonths(-5));
            _throughText = To ?? FormatDate(today);
            if (!TryParse(_fromText, out _from) || !TryParse(_throughText, out _through) || Preset is not (null or ""))
            {
                _rangeError = "Enter valid dates in YYYY-MM-DD format.";
                return false;
            }
        }
        if (_through < _from || _from.Year < 2000 || _through.Year > 2100 || _through.DayNumber - _from.DayNumber > 732)
        {
            _rangeError = "Choose an ordered date range of up to two years between 2000 and 2100.";
            return false;
        }
        return true;
    }

    private string PresetUrl(string preset) => $"/plans/{PlanId}/reports?preset={preset}";
    private string RangeUrl(DateOnly from, DateOnly through) => $"/plans/{PlanId}/reports?from={FormatDate(from)}&to={FormatDate(through)}";
    private string RegisterUrl(Guid categoryId) => $"/plans/{PlanId}/accounts?category={categoryId}&from={FormatDate(_from)}&to={FormatDate(_through)}";
    private decimal Budgeted(Guid categoryId) => _plan!.Allocations.Where(item => item.CategoryId == categoryId
        && item.Month >= BudgetFacts.Month(_from) && item.Month <= BudgetFacts.Month(_through)).Sum(item => item.Amount);
    private string Share(decimal amount) => TotalSpending == 0 ? "—" : (amount / TotalSpending).ToString("P1", Culture);
    private string CashFlowWidth(decimal amount) => (Math.Abs(amount) / MaxCashFlow).ToString("P0", CultureInfo.InvariantCulture);
    private static string Comparison(decimal current, decimal previous)
    {
        var difference = current - previous;
        if (difference == 0) { return "No change from previous period"; }
        var sign = difference > 0 ? "+" : "−";
        return $"{sign}{Money(Math.Abs(difference))} vs previous period";
    }
    private decimal PreviousIncome => _previousMonths.Sum(item => item.Income);
    private decimal PreviousSpending => _previousMonths.Sum(item => item.Expense);
    private decimal PreviousNetWorth => _previousMonths.Count > 0 ? _previousMonths[^1].NetWorth : 0;

    private string BuildCsv()
    {
        var csv = new StringBuilder();
        csv.AppendLine("Type,Period,Category,Income,Spending,Net income,Assets,Debt,Net worth,Budgeted,Share");
        foreach (var month in _months)
        {
            csv.AppendLine(CsvRow("Month", month.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture), "", Amount(month.Income), Amount(month.Expense),
                Amount(month.NetIncome), Amount(month.Assets), Amount(month.Debt), Amount(month.NetWorth), "", ""));
        }
        foreach (var row in _spending)
        {
            csv.AppendLine(CsvRow("Category", $"{FormatDate(_from)} to {FormatDate(_through)}", row.Name, "", Amount(row.Amount), "", "", "", "",
                Amount(Budgeted(row.CategoryId)), Share(row.Amount)));
        }
        return csv.ToString();
    }

    private static string CsvRow(params string[] values) => string.Join(',', values.Select(CsvCell));
    private static string CsvCell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') { value = "'" + value; }
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string FormatCoordinate(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static bool TryParse(string value, out DateOnly date) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    private sealed record ChartPoint(string X, string Y, DateOnly Month, decimal Value);
}
