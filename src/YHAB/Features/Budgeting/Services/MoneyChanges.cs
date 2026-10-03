using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class MoneyChanges
{
    public static BudgetChangeOutcome Assign(PlanSnapshot plan, AssignMoney command)
    {
        if (!plan.Categories.Any(item => item.Id == command.CategoryId) || !ValidMonth(command.Month)
            || !CatalogChanges.ValidMoney(command.Amount))
        {
            return new InvalidBudgetChange("Choose a category, calendar month, and valid assigned amount.");
        }

        return SetAllocation(plan, new(command.CategoryId, command.Month, command.Amount, command.Snoozed));
    }

    public static BudgetChangeOutcome Move(PlanSnapshot plan, MoveMoney command, DateOnly today)
    {
        if (command.FromCategoryId == command.ToCategoryId || !ValidMonth(command.Month) || command.Amount <= 0
            || !CatalogChanges.ValidMoney(command.Amount) || !ValidCategory(plan, command.FromCategoryId) || !ValidCategory(plan, command.ToCategoryId))
        {
            return new InvalidBudgetChange("Choose different source and destination categories and a positive amount.");
        }

        var month = BudgetCalculator.Calculate(plan, command.Month, today);
        var available = command.FromCategoryId is { } sourceId
            ? month.Categories.Single(item => item.Category.Id == sourceId).Available : month.ReadyToAssign;
        if (available < command.Amount)
        {
            return new InvalidBudgetChange("There is not enough available money in the selected source.");
        }

        var fromAssigned = plan.Allocations.SingleOrDefault(item => item.CategoryId == command.FromCategoryId && item.Month == command.Month)?.Amount ?? 0;
        var toAssigned = plan.Allocations.SingleOrDefault(item => item.CategoryId == command.ToCategoryId && item.Month == command.Month)?.Amount ?? 0;
        if (!CatalogChanges.ValidMoney(fromAssigned - command.Amount) || !CatalogChanges.ValidMoney(toAssigned + command.Amount))
        {
            return new InvalidBudgetChange("This move would exceed the supported assigned amount.");
        }

        if (command.FromCategoryId is { } from)
        {
            plan = Adjust(plan, from, command.Month, -command.Amount);
        }

        if (command.ToCategoryId is { } to)
        {
            plan = Adjust(plan, to, command.Month, command.Amount);
        }

        return plan;
    }

    public static BudgetChangeOutcome AutoAssign(PlanSnapshot plan, AutoAssign command, DateOnly today)
    {
        if (!ValidMonth(command.Month))
        {
            return new InvalidBudgetChange("Choose a calendar month.");
        }

        var month = BudgetCalculator.Calculate(plan, command.Month, today);
        var remaining = Math.Max(0, month.ReadyToAssign);
        foreach (var row in month.Categories.OrderByDescending(item => item.Available < 0)
            .ThenBy(item => item.Category.Target?.DueDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.Category.SortOrder))
        {
            var need = Math.Max(Math.Max(0, -row.Available), row.TargetNeeded);
            if (row.Snoozed || row.Category.Hidden || plan.Groups.Any(item => item.Id == row.Category.GroupId && item.Hidden) || need == 0 || remaining == 0)
            {
                continue;
            }

            var amount = Math.Min(Math.Min(remaining, need), AmountExpression.MaximumAmount - row.Assigned);
            plan = Adjust(plan, row.Category.Id, command.Month, amount);
            remaining -= amount;
        }

        return plan;
    }

    public static BudgetChangeOutcome Reconcile(PlanSnapshot plan, ReconcileAccount command, DateOnly today)
    {
        var account = plan.Accounts.SingleOrDefault(item => item.Id == command.AccountId && !item.Closed);
        if (account is null || !CatalogChanges.ValidDate(command.Date) || command.Date > today || command.Date < account.OpenedOn
            || !CatalogChanges.ValidMoney(command.ClearedBalance))
        {
            return new InvalidBudgetChange("Choose an open account, valid reconciliation date, and statement balance.");
        }

        var difference = command.ClearedBalance - BudgetFacts.Balance(plan, account, command.Date).Cleared;
        if (!CatalogChanges.ValidMoney(difference))
        {
            return new InvalidBudgetChange("The reconciliation difference exceeds the supported transaction amount. Check the statement balance and your entries.");
        }

        if (difference != 0 && !command.CreateAdjustment)
        {
            return new InvalidBudgetChange($"Your cleared balance differs by {BudgetFacts.Money(difference)}. Check transactions or explicitly create an adjustment.");
        }

        var entries = plan.Transactions.Select(item => ReconcileEntry(item, account.Id, command.Date)).ToList();
        if (difference != 0)
        {
            // Adjustments to cash balances affect Ready to Assign, rather than pretending to be spending.
            entries.Add(new(Guid.NewGuid(), account.Id, command.Date, "Reconciliation adjustment", "Balance correction",
                difference, null, ClearingState.Reconciled, ClearingState.Uncleared, false, string.Empty, []));
        }

        return plan with { Transactions = entries };
    }

    private static TransactionData ReconcileEntry(TransactionData entry, Guid accountId, DateOnly through)
    {
        if (entry.Date > through || entry.Repeat != RepeatFrequency.None)
        {
            return entry;
        }

        if (entry.AccountId == accountId && entry.State == ClearingState.Cleared)
        {
            entry = entry with { State = ClearingState.Reconciled };
        }

        if (entry.TransferAccountId == accountId && entry.TransferState == ClearingState.Cleared)
        {
            entry = entry with { TransferState = ClearingState.Reconciled };
        }

        return entry;
    }

    private static bool ValidMonth(DateOnly month) => CatalogChanges.ValidDate(month) && month.Day == 1;

    private static bool ValidCategory(PlanSnapshot plan, Guid? categoryId)
        => categoryId is null || plan.Categories.Any(item => item.Id == categoryId);

    private static PlanSnapshot Adjust(PlanSnapshot plan, Guid categoryId, DateOnly month, decimal amount)
    {
        var previous = plan.Allocations.SingleOrDefault(item => item.CategoryId == categoryId && item.Month == month)
            ?? new(categoryId, month, 0);
        return SetAllocation(plan, previous with { Amount = previous.Amount + amount });
    }

    private static PlanSnapshot SetAllocation(PlanSnapshot plan, AllocationData allocation)
        => plan with
        {
            Allocations = plan.Allocations.Where(item => item.CategoryId != allocation.CategoryId || item.Month != allocation.Month)
                .Append(allocation).ToArray(),
        };
}
