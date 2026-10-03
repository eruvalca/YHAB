using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class CatalogChanges
{
    public static BudgetChangeOutcome Save(PlanSnapshot plan, SaveAccount command, DateOnly today)
    {
        var account = command.Account;
        if (account is null || !ValidName(account.Name) || !Enum.IsDefined(account.Kind) || account.Notes is null || account.Notes.Length > 4000)
        {
            return new InvalidBudgetChange("Enter an account name of 1–100 characters and a valid account type.");
        }

        if (!ValidMoney(account.OpeningBalance) || !ValidMoney(account.MinimumPayment) || account.MinimumPayment < 0
            || account.InterestRate is < 0 or > 100 || !ValidDate(account.OpenedOn) || account.OpenedOn > today)
        {
            return new InvalidBudgetChange("Check the opening date, balance, interest rate, and minimum payment.");
        }

        var existing = plan.Accounts.SingleOrDefault(item => item.Id == account.Id);
        if (existing is not null && existing.Kind != account.Kind)
        {
            return new InvalidBudgetChange("Account type cannot be changed after creation. Create another account instead.");
        }

        if (OpeningChangeAffectsReconciliation(plan, existing, account))
        {
            return new InvalidBudgetChange("Unreconcile this account's transactions before changing its opening balance or date.");
        }

        account = account with { Id = account.Id == Guid.Empty ? Guid.NewGuid() : account.Id, Name = account.Name.Trim() };
        if (plan.Transactions.Any(item => (item.AccountId == account.Id || item.TransferAccountId == account.Id) && item.Date < account.OpenedOn))
        {
            return new InvalidBudgetChange("The opening date must be on or before this account's first transaction.");
        }

        if (account.Closed && BudgetFacts.Balance(plan, account, today).Working != 0)
        {
            return new InvalidBudgetChange("Bring the account balance to zero before closing it.");
        }

        var categories = plan.Categories.ToList();
        var groups = plan.Groups.ToList();
        if (BudgetFacts.IsCredit(account.Kind))
        {
            var payment = categories.SingleOrDefault(item => item.CreditAccountId == account.Id);
            if (payment is null)
            {
                var group = groups.FirstOrDefault(item => string.Equals(item.Name, "Credit card payments", StringComparison.Ordinal));
                if (group is null)
                {
                    group = new(Guid.NewGuid(), "Credit card payments", -1);
                    groups.Add(group);
                }

                categories.Add(new(Guid.NewGuid(), group.Id, account.Name, string.Empty, categories.Count, false, account.Id, null));
            }
            else
            {
                categories[categories.IndexOf(payment)] = payment with { Name = account.Name };
            }
        }

        return plan with { Accounts = Replace(plan.Accounts, account, item => item.Id), Categories = categories, Groups = groups };
    }

    public static BudgetChangeOutcome Save(PlanSnapshot plan, SaveGroup command)
    {
        var group = command.Group;
        if (group is null || !ValidName(group.Name))
        {
            return new InvalidBudgetChange("Enter a group name of 1–100 characters.");
        }

        group = group with { Id = group.Id == Guid.Empty ? Guid.NewGuid() : group.Id, Name = group.Name.Trim() };
        return plan with { Groups = Replace(plan.Groups, group, item => item.Id) };
    }

    public static BudgetChangeOutcome Save(PlanSnapshot plan, SaveCategory command)
    {
        var category = command.Category;
        if (category is null || !ValidName(category.Name) || category.Notes is null || category.Notes.Length > 4000
            || !plan.Groups.Any(item => item.Id == category.GroupId))
        {
            return new InvalidBudgetChange("Enter a category name, valid group, and notes of at most 4,000 characters.");
        }

        var existing = plan.Categories.SingleOrDefault(item => item.Id == category.Id);
        if (category.CreditAccountId != existing?.CreditAccountId)
        {
            return new InvalidBudgetChange("Payment categories are managed by their credit accounts.");
        }

        if (!ValidTarget(category.Target))
        {
            return new InvalidBudgetChange("Check the target amount, start month, due date, and repeat interval.");
        }

        category = category with { Id = category.Id == Guid.Empty ? Guid.NewGuid() : category.Id, Name = category.Name.Trim() };
        return plan with { Categories = Replace(plan.Categories, category, item => item.Id) };
    }

    public static BudgetChangeOutcome Remove(PlanSnapshot plan, RemoveCategory command)
    {
        var category = plan.Categories.SingleOrDefault(item => item.Id == command.CategoryId);
        if (category is null || category.CreditAccountId.HasValue)
        {
            return new InvalidBudgetChange("Choose a spending category. Credit payment categories cannot be deleted.");
        }

        var used = plan.Allocations.Any(item => item.CategoryId == category.Id)
            || plan.Transactions.Any(item => item.Splits.Any(split => split.CategoryId == category.Id));
        if (used && (command.ReplacementCategoryId == category.Id
            || !plan.Categories.Any(item => item.Id == command.ReplacementCategoryId && item.CreditAccountId is null)))
        {
            return new InvalidBudgetChange("Choose another category to receive this category's history and assigned money.");
        }

        var allocations = plan.Allocations.Select(item => item.CategoryId == category.Id
                ? item with { CategoryId = command.ReplacementCategoryId!.Value } : item)
            .GroupBy(item => (item.CategoryId, item.Month))
            .Select(items => new AllocationData(items.Key.CategoryId, items.Key.Month, items.Sum(item => item.Amount), items.All(item => item.Snoozed)))
            .ToArray();
        var transactions = plan.Transactions.Select(item => item with
        {
            Splits = item.Splits.Select(split => split.CategoryId == category.Id
                ? split with { CategoryId = command.ReplacementCategoryId } : split).ToArray(),
        }).ToArray();
        if (allocations.Any(item => !ValidMoney(item.Amount)))
        {
            return new InvalidBudgetChange("The combined assignments exceed the supported amount.");
        }
        return plan with { Categories = plan.Categories.Where(item => item.Id != category.Id).ToArray(), Allocations = allocations, Transactions = transactions };
    }

    internal static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;

    private static bool OpeningChangeAffectsReconciliation(PlanSnapshot plan, AccountData? existing, AccountData account)
        => existing is not null && (existing.OpeningBalance != account.OpeningBalance || existing.OpenedOn != account.OpenedOn)
            && plan.Transactions.Any(item => (item.AccountId == account.Id && item.State == ClearingState.Reconciled)
                || (item.TransferAccountId == account.Id && item.TransferState == ClearingState.Reconciled));

    internal static bool ValidMoney(decimal amount) => amount is >= -AmountExpression.MaximumAmount and <= AmountExpression.MaximumAmount && decimal.Round(amount, 2) == amount;

    internal static bool ValidDate(DateOnly date) => date.Year is >= 2000 and <= 2100;

    internal static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> list, T value, Func<T, Guid> key)
        => list.Where(item => key(item) != key(value)).Append(value).ToArray();

    private static bool ValidTarget(TargetData? target)
    {
        if (target is null)
        {
            return true;
        }

        return Enum.IsDefined(target.Kind) && Enum.IsDefined(target.Cadence) && Enum.IsDefined(target.Weekday)
            && ValidMoney(target.Amount) && target.Amount > 0 && ValidDate(target.StartMonth) && target.StartMonth.Day == 1
            && target.RepeatEveryMonths is >= 0 and <= 120
            && (target.DueDate is null || (ValidDate(target.DueDate.Value) && target.DueDate >= target.StartMonth))
            && (target.Cadence != TargetCadence.Yearly || target.DueDate.HasValue);
    }
}
