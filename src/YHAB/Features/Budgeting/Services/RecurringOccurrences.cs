using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class RecurringOccurrences
{
    public static async Task<IReadOnlySet<RecurringOccurrence>> ExistingAsync(ApplicationDbContext database,
        PlanSnapshot before, PlanSnapshot proposed, CancellationToken token)
    {
        var existingIds = before.Transactions.Select(item => item.Id).ToHashSet();
        var candidates = proposed.Transactions.Where(item => item.SourceTemplateId.HasValue && !existingIds.Contains(item.Id)).ToArray();
        if (candidates.Length == 0)
        {
            return new HashSet<RecurringOccurrence>();
        }

        // The pure proposal contains at most one posting batch. Read only its stable
        // identities; edited occurrence dates and years of prior postings are irrelevant.
        var templates = candidates.Select(item => item.SourceTemplateId!.Value).ToArray();
        var dates = candidates.Select(item => item.ScheduledDate!.Value).ToArray();
        var matches = await database.Database.SqlQuery<RecurringOccurrence>($"""
            SELECT entry."SourceTemplateId" AS "TemplateId", entry."ScheduledDate"
            FROM "BudgetTransaction" AS entry
            JOIN unnest({templates}, {dates}) AS due("TemplateId", "ScheduledDate")
                ON entry."SourceTemplateId" = due."TemplateId" AND entry."ScheduledDate" = due."ScheduledDate"
            WHERE entry."PlanId" = {before.Id}
            """).ToArrayAsync(token);
        return matches.ToHashSet();
    }
}
