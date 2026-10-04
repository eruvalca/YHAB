using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class ReportQueries
{
    public static async Task<ReportView> ReadAsync(ApplicationDbContext database, PlanSnapshot catalog, DateOnly fromDate, DateOnly through, CancellationToken token)
    {
        var entries = database.Set<BudgetTransaction>().Where(entry => entry.PlanId == catalog.Id
            && entry.Repeat == RepeatFrequency.None && entry.Date <= through);
        var movements = entries.Select(entry => new { entry.AccountId, entry.Date, entry.Amount })
            .Concat(entries.Where(entry => entry.TransferAccountId.HasValue)
                .Select(entry => new { AccountId = entry.TransferAccountId!.Value, entry.Date, Amount = -entry.Amount }));
        // All earlier movement is one bucket per account, rather than one bucket
        // per historical month. Only requested months cross the database boundary.
        var balances = await movements.GroupBy(item => new
        {
            item.AccountId,
            Year = item.Date < fromDate ? 0 : item.Date.Year,
            Month = item.Date < fromDate ? 0 : item.Date.Month,
        }).Select(group => new { group.Key.AccountId, group.Key.Year, group.Key.Month, Amount = group.Sum(item => item.Amount) }).ToArrayAsync(token);
        var budgetAccounts = catalog.Accounts.Where(account => BudgetFacts.IsBudget(account.Kind)).Select(account => account.Id).ToArray();
        var flow = from entry in entries.Where(entry => entry.Date >= fromDate)
                   join split in database.Set<BudgetSplit>().Where(split => split.PlanId == catalog.Id)
                       on entry.Id equals split.TransactionId
                   select new
                   {
                       entry.Date,
                       split.CategoryId,
                       Amount = split.Amount * ((budgetAccounts.Contains(entry.AccountId) ? 1 : 0)
                           - (entry.TransferAccountId.HasValue && budgetAccounts.Contains(entry.TransferAccountId.Value) ? 1 : 0)),
                   };
        var categories = await flow.GroupBy(item => new { item.CategoryId, item.Date.Year, item.Date.Month })
            .Select(group => new ReportCategoryMonth(group.Key.CategoryId, new DateOnly(group.Key.Year, group.Key.Month, 1), group.Sum(item => item.Amount)))
            .ToArrayAsync(token);
        var totals = new ReportTotals(balances.Where(item => item.Year == 0).ToDictionary(item => item.AccountId, item => item.Amount),
            balances.Where(item => item.Year != 0).Select(item => new ReportAccountMonth(item.AccountId, new(item.Year, item.Month, 1), item.Amount)).ToArray(), categories);
        return ReportCalculator.FromTotals(catalog, fromDate, through, totals);
    }
}
