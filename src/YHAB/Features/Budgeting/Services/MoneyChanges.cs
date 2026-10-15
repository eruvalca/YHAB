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

    public static BudgetChangeOutcome Move(PlanSnapshot plan, MoveMoney command, DateOnly today, BudgetMonth? projection = null)
    {
        if (command.FromCategoryId == command.ToCategoryId || !ValidMonth(command.Month) || command.Amount <= 0
            || !CatalogChanges.ValidMoney(command.Amount) || !ValidCategory(plan, command.FromCategoryId) || !ValidCategory(plan, command.ToCategoryId))
        {
            return new InvalidBudgetChange("Choose different source and destination categories and a positive amount.");
        }

        var month = projection ?? BudgetCalculator.Calculate(plan, command.Month, today);
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

    public static BudgetChangeOutcome AutoAssign(PlanSnapshot plan, AutoAssign command, DateOnly today, BudgetMonth? projection = null)
    {
        if (!ValidMonth(command.Month))
        {
            return new InvalidBudgetChange("Choose a calendar month.");
        }

        var month = projection ?? BudgetCalculator.Calculate(plan, command.Month, today);
        var remaining = Math.Max(0, month.ReadyToAssign);
        var groupOrder = plan.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Id)
            .Select((item, index) => (item.Id, index)).ToDictionary(item => item.Id, item => item.index);
        foreach (var row in month.Categories.OrderByDescending(item => item.Available < 0)
            .ThenBy(item => item.Category.Target?.DueDate ?? DateOnly.MaxValue)
            .ThenBy(item => groupOrder[item.Category.GroupId])
            .ThenBy(item => item.Category.SortOrder).ThenBy(item => item.Category.Id))
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

    private static bool ValidMonth(DateOnly month) => CatalogChanges.ValidDate(month) && month.Day == 1;

    private static bool ValidCategory(PlanSnapshot plan, Guid? categoryId)
        => categoryId is not { } id || plan.Categories.Any(item => item.Id == id);

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
