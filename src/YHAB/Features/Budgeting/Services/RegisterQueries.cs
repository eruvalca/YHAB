using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class RegisterQueries
{
    public static async Task<RegisterPage> ReadAsync(ApplicationDbContext database, Guid id, long version, RegisterQuery request, CancellationToken token)
    {
        var all = database.Set<BudgetTransaction>().Where(entry => entry.PlanId == id);
        var accounts = database.Set<BudgetAccount>().Where(item => item.PlanId == id);
        var categories = database.Set<BudgetCategory>().Where(item => item.PlanId == id);
        var splits = database.Set<BudgetSplit>().Where(item => item.PlanId == id);
        var account = request.AccountId.HasValue ? await accounts.SingleOrDefaultAsync(item => item.Id == request.AccountId, token) : null;
        if (request.AccountId.HasValue && account is null) { throw new BudgetRequestException(404, "Account not found."); }
        var query = all.Where(entry => !request.AccountId.HasValue || entry.AccountId == request.AccountId || entry.TransferAccountId == request.AccountId);
        query = query.Where(entry => (!request.From.HasValue || entry.Date >= request.From) && (!request.Through.HasValue || entry.Date <= request.Through));
        query = Filter(query, request);
        query = Search(query, accounts, categories, splits, request);
        var total = await query.CountAsync(token);
        var candidates = query.Select(entry => new Candidate
        {
            Entry = entry,
            Amount = request.AccountId.HasValue && entry.TransferAccountId == request.AccountId ? -entry.Amount : entry.Amount,
            Payee = entry.TransferAccountId.HasValue ? "Transfer: " + accounts.Where(item => item.Id == entry.AccountId).Select(item => item.Name).First()
                + " ↔ " + accounts.Where(item => item.Id == entry.TransferAccountId).Select(item => item.Name).First() : entry.Payee == "" ? "No payee" : entry.Payee,
        });
        candidates = After(candidates, request);
        var ordered = Sort(candidates, request.Sort);
        var selected = await ordered.Take(request.PageSize + 1).ToArrayAsync(token);
        var visible = selected.Take(request.PageSize).ToArray();
        var ids = visible.Select(item => item.Entry.Id).ToArray();
        var running = account is null ? new Dictionary<Guid, decimal>() : await RunningBalancesAsync(database, id, account.Id, ids, token);
        var selectedSplits = (await splits.Where(item => ids.Contains(item.TransactionId)).ToArrayAsync(token)).ToLookup(item => item.TransactionId);
        var rows = visible.Select(item => new RegisterRow(BudgetSnapshotMapping.ToData(item.Entry,
            selectedSplits[item.Entry.Id].Select(BudgetSnapshotMapping.ToData).ToArray()), (account?.OpeningBalance ?? 0) + running.GetValueOrDefault(item.Entry.Id))).ToArray();
        var last = visible.LastOrDefault();
        var next = selected.Length > request.PageSize && last is not null ? new RegisterCursor(last.Entry.Date, last.Entry.Sequence, last.Entry.Id, last.Payee, last.Amount) : null;
        return new RegisterPage(version, rows, total, next);
    }
    private static async Task<Dictionary<Guid, decimal>> RunningBalancesAsync(ApplicationDbContext database, Guid planId, Guid accountId, Guid[] ids, CancellationToken token)
    {
        // A single ordered pass in PostgreSQL, before filtering to the visible IDs. LINQ's correlated
        // sums scanned the same history once for every displayed row on large accounts.
        return await database.Database.SqlQuery<RunningBalanceRow>($"""
            SELECT "Id", "Balance" FROM (
                SELECT "Id", SUM(CASE WHEN "AccountId" = {accountId} THEN "Amount" ELSE -"Amount" END)
                    OVER (ORDER BY "Date", "Sequence", "Id" ROWS UNBOUNDED PRECEDING) AS "Balance"
                FROM "BudgetTransaction"
                WHERE "PlanId" = {planId} AND "Repeat" = 0
                    AND ("AccountId" = {accountId} OR "TransferAccountId" = {accountId})
            ) AS ledger WHERE "Id" = ANY({ids})
            """).ToDictionaryAsync(item => item.Id, item => item.Balance, token);
    }

    private sealed record RunningBalanceRow(Guid Id, decimal Balance);
    private static IQueryable<BudgetTransaction> Filter(IQueryable<BudgetTransaction> query, RegisterQuery request)
    {
        return request.Filter switch
        {
            "Recurring" => query.Where(entry => entry.Repeat != RepeatFrequency.None),
            "Needs approval" => query.Where(entry => entry.Repeat == RepeatFrequency.None && entry.NeedsApproval),
            "Uncleared" => query.Where(entry => entry.Repeat == RepeatFrequency.None && (entry.TransferAccountId == request.AccountId && request.AccountId.HasValue ? entry.TransferState : entry.State) == ClearingState.Uncleared),
            "Cleared" => query.Where(entry => entry.Repeat == RepeatFrequency.None && (entry.TransferAccountId == request.AccountId && request.AccountId.HasValue ? entry.TransferState : entry.State) == ClearingState.Cleared),
            "Reconciled" => query.Where(entry => entry.Repeat == RepeatFrequency.None && (entry.TransferAccountId == request.AccountId && request.AccountId.HasValue ? entry.TransferState : entry.State) == ClearingState.Reconciled),
            _ => query.Where(entry => entry.Repeat == RepeatFrequency.None),
        };

    }
    private static IQueryable<BudgetTransaction> Search(IQueryable<BudgetTransaction> query, IQueryable<BudgetAccount> accounts,
        IQueryable<BudgetCategory> categories, IQueryable<BudgetSplit> splits, RegisterQuery request)
    {
        var search = request.Search.Trim();
        if (search.Length > 0)
        {
            var pattern = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var amountSearch = AmountExpression.TryEvaluate(search, out var amount);
            var unassigned = "Ready to assign".Contains(search, StringComparison.OrdinalIgnoreCase);
            var adjustment = "Transfer / adjustment".Contains(search, StringComparison.OrdinalIgnoreCase);
            var noPayee = "No payee".Contains(search, StringComparison.OrdinalIgnoreCase);
            query = query.Where(entry => EF.Functions.ILike(entry.Payee, pattern, "\\") || EF.Functions.ILike(entry.Memo, pattern, "\\") || EF.Functions.ILike(entry.Flag, pattern, "\\")
                || accounts.Any(item => (item.Id == entry.AccountId || item.Id == entry.TransferAccountId) && EF.Functions.ILike(item.Name, pattern, "\\"))
                || splits.Any(split => split.TransactionId == entry.Id && categories.Any(category => category.Id == split.CategoryId && EF.Functions.ILike(category.Name, pattern, "\\")))
                || (unassigned && splits.Any(split => split.TransactionId == entry.Id && split.CategoryId == null))
                || (adjustment && !splits.Any(split => split.TransactionId == entry.Id))
                || (noPayee && entry.TransferAccountId == null && entry.Payee == "")
                || (amountSearch && (request.AccountId.HasValue && entry.TransferAccountId == request.AccountId ? -entry.Amount : entry.Amount) == amount));
        }

        return query;
    }
    private static IOrderedQueryable<Candidate> Sort(IQueryable<Candidate> candidates, string sort)
    {
        return sort switch
        {
            "Payee" => candidates.OrderBy(item => item.Payee).ThenByDescending(item => item.Entry.Date).ThenByDescending(item => item.Entry.Sequence).ThenByDescending(item => item.Entry.Id),
            "Amount" => candidates.OrderBy(item => item.Amount).ThenByDescending(item => item.Entry.Date).ThenByDescending(item => item.Entry.Sequence).ThenByDescending(item => item.Entry.Id),
            "Oldest first" => candidates.OrderBy(item => item.Entry.Date).ThenBy(item => item.Entry.Sequence).ThenBy(item => item.Entry.Id),
            _ => candidates.OrderByDescending(item => item.Entry.Date).ThenByDescending(item => item.Entry.Sequence).ThenByDescending(item => item.Entry.Id),
        };

    }
    private static IQueryable<Candidate> After(IQueryable<Candidate> candidates, RegisterQuery request)
    {
        if (request.After is not { } after) { return candidates; }
        return request.Sort switch
        {
            "Payee" => AfterPayee(candidates, after),
            "Amount" => AfterAmount(candidates, after),
            "Oldest first" => candidates.Where(item => item.Entry.Date > after.Date || (item.Entry.Date == after.Date
                && (item.Entry.Sequence > after.Sequence || (item.Entry.Sequence == after.Sequence && item.Entry.Id.CompareTo(after.Id) > 0)))),
            _ => candidates.Where(item => item.Entry.Date < after.Date || (item.Entry.Date == after.Date
                && (item.Entry.Sequence < after.Sequence || (item.Entry.Sequence == after.Sequence && item.Entry.Id.CompareTo(after.Id) < 0)))),
        };
    }

    // SQL ORDER BY and cursor comparison must use the same database collation.
    [SuppressMessage("Globalization", "CA1309:Use ordinal string comparison", Justification = "Translated by EF to PostgreSQL comparisons using the same collation as ORDER BY; StringComparison overloads are not SQL translatable.")]
    private static IQueryable<Candidate> AfterPayee(IQueryable<Candidate> candidates, RegisterCursor after)
        => candidates.Where(item => string.Compare(item.Payee, after.Payee) > 0 || (item.Payee == after.Payee
            && (item.Entry.Date < after.Date || (item.Entry.Date == after.Date && (item.Entry.Sequence < after.Sequence || (item.Entry.Sequence == after.Sequence && item.Entry.Id.CompareTo(after.Id) < 0))))));

    private static IQueryable<Candidate> AfterAmount(IQueryable<Candidate> candidates, RegisterCursor after)
        => candidates.Where(item => item.Amount > after.Amount || (item.Amount == after.Amount
            && (item.Entry.Date < after.Date || (item.Entry.Date == after.Date && (item.Entry.Sequence < after.Sequence || (item.Entry.Sequence == after.Sequence && item.Entry.Id.CompareTo(after.Id) < 0))))));

    private sealed class Candidate
    {
        public required BudgetTransaction Entry { get; init; }
        public required string Payee { get; init; }
        public decimal Amount { get; init; }
    }
}
