using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class PlanCommandHandler
{
    public static BudgetChangeOutcome Apply(CommandIds ids, PlanSnapshot plan, PlanCommand command, DateOnly today,
        BudgetMonth? projection = null, IReadOnlySet<RecurringOccurrence>? posted = null)
        => command switch
        {
            SaveAccount save => CatalogChanges.Save(ids, plan, save, today),
            SaveGroup save => CatalogChanges.Save(ids, plan, save),
            SaveCategory save => CatalogChanges.Save(ids, plan, save),
            RemoveCategory remove => CatalogChanges.Remove(plan, remove),
            SaveTransaction save => TransactionChanges.Save(ids, plan, save, today, posted),
            DeleteTransactions delete => TransactionChanges.Delete(plan, delete),
            UpdateTransactionStates update => TransactionChanges.UpdateStates(plan, update),
            AssignMoney assign => MoneyChanges.Assign(plan, assign),
            MoveMoney move => MoneyChanges.Move(plan, move, today, projection),
            AutoAssign auto => MoneyChanges.AutoAssign(plan, auto, today, projection),
            ReconcileAccount reconcile => MoneyChanges.Reconcile(ids, plan, reconcile, today),
            UpdatePlan update => Update(plan, update),
            RenamePayee rename => Rename(plan, rename),
            PostRecurring post when post.ThroughDate <= today && CatalogChanges.ValidDate(post.ThroughDate)
                => TransactionChanges.PostDue(ids, plan, post.ThroughDate, posted),
            _ => new InvalidBudgetChange("This plan operation is not supported."),
        };

    private static BudgetChangeOutcome Update(PlanSnapshot plan, UpdatePlan command)
    {
        if (!CatalogChanges.ValidName(command.Name) || command.Notes is null || command.Notes.Length > 4000)
        {
            return new InvalidBudgetChange("Enter a plan name of 1–100 characters and notes of at most 4,000 characters.");
        }

        return plan with { Name = command.Name.Trim(), Notes = command.Notes };
    }

    private static BudgetChangeOutcome Rename(PlanSnapshot plan, RenamePayee command)
    {
        var error = PayeePatch.ValidationError(command);
        if (error is not null)
        {
            return new InvalidBudgetChange(error);
        }
        var changes = PayeePatch.Rename(plan.Transactions.Select(item => new TransactionPayee(item.Id, item.Payee)).ToArray(), command)
            .After.ToDictionary(item => item.Id, item => item.Payee);
        return plan with
        {
            Transactions = plan.Transactions.Select(item => changes.TryGetValue(item.Id, out var name) ? item with { Payee = name } : item).ToArray(),
        };
    }

    public static string Describe(PlanCommand command) => command switch
    {
        SaveAccount => "Updated account",
        SaveGroup => "Updated category group",
        SaveCategory => "Updated category or target",
        RemoveCategory => "Merged category",
        SaveTransaction => "Saved transaction",
        DeleteTransactions => "Deleted transactions",
        UpdateTransactionStates => "Updated transaction status",
        AssignMoney => "Assigned money",
        MoveMoney => "Moved money",
        AutoAssign => "Funded underfunded categories",
        ReconcileAccount => "Reconciled account",
        UpdatePlan => "Updated plan",
        RenamePayee => "Renamed payee",
        PostRecurring => "Posted recurring transactions",
        _ => "Updated plan",
    };
}
