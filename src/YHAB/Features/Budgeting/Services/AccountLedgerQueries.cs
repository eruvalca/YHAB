using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class AccountLedgerQueries
{
    public static async Task<AccountLedgerFacts> ReadAsync(ApplicationDbContext database, Guid planId, Guid accountId, DateOnly through, CancellationToken token)
        => await database.Set<BudgetTransaction>().Where(item => item.PlanId == planId && (item.AccountId == accountId || item.TransferAccountId == accountId))
            .Select(item => new
            {
                item.Date,
                Posted = item.Repeat == RepeatFrequency.None && item.Date <= through,
                Amount = item.AccountId == accountId ? item.Amount : -item.Amount,
                State = item.AccountId == accountId ? item.State : item.TransferState,
            }).GroupBy(_ => 1).Select(group => new AccountLedgerFacts(group.Min(item => (DateOnly?)item.Date),
                group.Any(item => item.State == ClearingState.Reconciled),
                group.Sum(item => item.Posted ? item.Amount : 0),
                group.Sum(item => item.Posted && item.State != ClearingState.Uncleared ? item.Amount : 0)))
            .SingleOrDefaultAsync(token) ?? new(null, false, 0, 0);
}
