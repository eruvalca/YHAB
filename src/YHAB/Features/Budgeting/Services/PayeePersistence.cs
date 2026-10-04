using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class PayeePersistence
{
    public static async Task<BudgetPreparationOutcome> PrepareAsync(ApplicationDbContext database,
        PlanSnapshot current, RenamePayee command, CancellationToken token)
    {
        var error = PayeePatch.ValidationError(command);
        if (error is not null) { return new InvalidBudgetChange(error); }
        var entries = database.Set<BudgetTransaction>().Where(item => item.PlanId == current.Id);
        // Preserve .NET ordinal case matching, including Unicode, without relying
        // on the database's locale. Only distinct names cross this boundary.
        var names = (await entries.Select(item => item.Payee).Distinct().ToArrayAsync(token))
            .Where(name => string.Equals(name, command.OldName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var values = names.Length == 0 ? [] : await entries.Where(item => names.Contains(item.Payee))
            .Select(item => new TransactionPayee(item.Id, item.Payee)).ToArrayAsync(token);
        return new PreparedBudgetChange(current, PayeePatch.Rename(values, command));
    }

    public static async Task<bool> CanApplyAsync(ApplicationDbContext database, Guid planId, PayeePatch patch, CancellationToken token)
    {
        var ids = patch.Before.Select(item => item.Id).ToArray();
        var current = await database.Set<BudgetTransaction>().Where(item => item.PlanId == planId && ids.Contains(item.Id))
            .Select(item => new TransactionPayee(item.Id, item.Payee)).ToArrayAsync(token);
        return patch.CanApply(current);
    }

    public static async Task ApplyAsync(ApplicationDbContext database, Guid planId, PayeePatch patch, CancellationToken token)
    {
        if (patch.Before.Count == 0) { return; }
        var desired = patch.After.ToDictionary(item => item.Id, item => item.Payee);
        var ids = patch.Before.Select(item => item.Id).ToArray();
        var before = patch.Before.Select(item => item.Payee).ToArray();
        var after = patch.Before.Select(item => desired[item.Id]).ToArray();
        var changed = await database.Database.ExecuteSqlAsync($"""
            UPDATE "BudgetTransaction" AS entry SET "Payee" = change."After"
            FROM unnest({ids}, {before}, {after}) AS change("Id", "Before", "After")
            WHERE entry."PlanId" = {planId} AND entry."Id" = change."Id" AND entry."Payee" = change."Before"
            """, token);
        if (changed != ids.Length)
        {
            throw new DbUpdateConcurrencyException("A payee changed before the operation could be applied.");
        }
    }
}
