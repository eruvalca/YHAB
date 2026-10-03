using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class PlanCommandHandler
{
    public static BudgetChangeOutcome Apply(PlanSnapshot plan, PlanCommand command, DateOnly today)
        => command switch
        {
            SaveAccount save => CatalogChanges.Save(plan, save, today),
            SaveGroup save => CatalogChanges.Save(plan, save),
            SaveCategory save => CatalogChanges.Save(plan, save),
            RemoveCategory remove => CatalogChanges.Remove(plan, remove),
            SaveTransaction save => TransactionChanges.Save(plan, save, today),
            DeleteTransactions delete => TransactionChanges.Delete(plan, delete),
            UpdateTransactionStates update => TransactionChanges.UpdateStates(plan, update),
            AssignMoney assign => MoneyChanges.Assign(plan, assign),
            MoveMoney move => MoneyChanges.Move(plan, move, today),
            AutoAssign auto => MoneyChanges.AutoAssign(plan, auto, today),
            ReconcileAccount reconcile => MoneyChanges.Reconcile(plan, reconcile, today),
            UpdatePlan update => Update(plan, update),
            RenamePayee rename => Rename(plan, rename),
            PostRecurring post when post.ThroughDate <= today && CatalogChanges.ValidDate(post.ThroughDate)
                => TransactionChanges.PostDue(plan, post.ThroughDate),
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
        if (string.IsNullOrWhiteSpace(command.OldName) || string.IsNullOrWhiteSpace(command.NewName) || command.NewName.Length > 200)
        {
            return new InvalidBudgetChange("Enter an existing payee and a new name of at most 200 characters.");
        }

        return plan with
        {
            Transactions = plan.Transactions.Select(item => string.Equals(item.Payee, command.OldName, StringComparison.OrdinalIgnoreCase)
                ? item with { Payee = command.NewName.Trim() } : item).ToArray(),
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
