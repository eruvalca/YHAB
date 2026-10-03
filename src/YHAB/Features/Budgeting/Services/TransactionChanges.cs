using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class TransactionChanges
{
    private static readonly HashSet<string> _allowedFlags = new(StringComparer.Ordinal)
    {
        string.Empty, "Red", "Orange", "Yellow", "Green", "Blue", "Purple",
    };

    public static BudgetChangeOutcome Save(PlanSnapshot plan, SaveTransaction command, DateOnly today)
    {
        var entry = command.Transaction;
        var error = Validate(plan, entry, today);
        if (error is not null)
        {
            return new InvalidBudgetChange(error);
        }

        var existing = plan.Transactions.SingleOrDefault(item => item.Id == entry.Id);
        if (existing is not null && (existing.State == ClearingState.Reconciled || existing.TransferState == ClearingState.Reconciled))
        {
            return new InvalidBudgetChange("Unreconcile this transaction before editing it.");
        }

        if (entry.State == ClearingState.Reconciled || entry.TransferState == ClearingState.Reconciled)
        {
            return new InvalidBudgetChange("Use Reconcile to lock transactions after verifying your balance.");
        }

        entry = entry with
        {
            Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
            Payee = entry.Payee.Trim(),
            Splits = entry.Splits.Select(item => item with { Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id }).ToArray(),
            SourceTemplateId = existing?.SourceTemplateId,
            AnchorDate = entry.Repeat == RepeatFrequency.None ? null : entry.Date,
            Occurrence = 0,
        };
        var updated = plan with { Transactions = CatalogChanges.Replace(plan.Transactions, entry, item => item.Id) };
        return entry.Repeat == RepeatFrequency.None ? updated : PostDue(updated, today);
    }

    private static string? Validate(PlanSnapshot plan, TransactionData? entry, DateOnly today)
    {
        if (entry is null || !ValidFields(entry))
        {
            return "Check the transaction fields and split count.";
        }

        var account = plan.Accounts.SingleOrDefault(item => item.Id == entry.AccountId && !item.Closed);
        if (account is null || !CatalogChanges.ValidDate(entry.Date) || entry.Date < account.OpenedOn
            || (entry.Repeat == RepeatFrequency.None && entry.Date > today) || !CatalogChanges.ValidMoney(entry.Amount))
        {
            return "Choose an open account, a valid amount, and a date on or after its opening date. Future entries must repeat.";
        }

        var destination = plan.Accounts.SingleOrDefault(item => item.Id == entry.TransferAccountId && !item.Closed);
        if (entry.TransferAccountId.HasValue && (destination is null || destination.Id == account.Id || entry.Date < destination.OpenedOn))
        {
            return "Choose a different open transfer account in this plan.";
        }

        if (entry.Repeat != RepeatFrequency.None && entry.Date < today.AddYears(-5))
        {
            return "Start recurring transactions within the last five years.";
        }

        var bothBudget = destination is not null && BudgetFacts.IsBudget(account.Kind) && BudgetFacts.IsBudget(destination.Kind);
        var affectsBudget = BudgetFacts.IsBudget(account.Kind) || (destination is not null && BudgetFacts.IsBudget(destination.Kind));
        if (bothBudget || !affectsBudget)
        {
            return entry.Splits.Count == 0 ? null : "Transfers within your budget and tracking-only entries do not use categories.";
        }

        return ValidateSplits(plan, entry, account);
    }

    private static bool ValidFields(TransactionData entry)
        => entry.Splits is not null && entry.Splits.Count <= 100 && entry.Payee is not null
            && entry.Payee.Length <= 200 && entry.Memo is not null && entry.Memo.Length <= 4000 && entry.Flag is not null
            && _allowedFlags.Contains(entry.Flag) && Enum.IsDefined(entry.State) && Enum.IsDefined(entry.TransferState) && Enum.IsDefined(entry.Repeat);

    private static string? ValidateSplits(PlanSnapshot plan, TransactionData entry, AccountData account)
    {
        if (entry.Splits.Count == 0 || entry.Splits.Any(item => !CatalogChanges.ValidMoney(item.Amount))
            || entry.Splits.Sum(item => item.Amount) != entry.Amount)
        {
            return "Split amounts must add up exactly to the transaction amount.";
        }

        if (entry.Splits.Select(item => item.Id).Where(id => id != Guid.Empty).Distinct().Count()
            != entry.Splits.Count(item => item.Id != Guid.Empty))
        {
            return "Each split must have a unique identifier.";
        }

        var direction = BudgetFacts.IsBudget(account.Kind) ? 1 : -1;
        foreach (var split in entry.Splits)
        {
            if (!CatalogChanges.ValidMoney(split.Amount) || split.Memo is null || split.Memo.Length > 1000
                || (split.CategoryId is null && split.Amount * direction < 0)
                || (split.CategoryId.HasValue && !plan.Categories.Any(item => item.Id == split.CategoryId && item.CreditAccountId is null)))
            {
                return "Choose spending categories for outflows, valid split amounts, and split memos of at most 1,000 characters.";
            }

            if (plan.Transactions.Any(item => item.Id != entry.Id && item.Splits.Any(other => other.Id == split.Id)))
            {
                return "A split cannot belong to another transaction.";
            }
        }

        return null;
    }

    public static BudgetChangeOutcome Delete(PlanSnapshot plan, DeleteTransactions command)
    {
        if (command.TransactionIds is null || command.TransactionIds.Count == 0
            || command.TransactionIds.Any(id => !plan.Transactions.Any(item => item.Id == id)))
        {
            return new InvalidBudgetChange("Select transactions from this plan.");
        }

        if (plan.Transactions.Any(item => command.TransactionIds.Contains(item.Id)
            && (item.State == ClearingState.Reconciled || item.TransferState == ClearingState.Reconciled)))
        {
            return new InvalidBudgetChange("Unreconcile selected transactions before deleting them.");
        }

        return plan with { Transactions = plan.Transactions.Where(item => !command.TransactionIds.Contains(item.Id)).ToArray() };
    }

    public static BudgetChangeOutcome UpdateStates(PlanSnapshot plan, UpdateTransactionStates command)
    {
        if (command.TransactionIds is null || command.TransactionIds.Count == 0
            || command.TransactionIds.Any(id => !plan.Transactions.Any(item => item.Id == id))
            || (command.AccountId.HasValue && command.TransactionIds.Any(id => !plan.Transactions.Any(item => item.Id == id
                && (item.AccountId == command.AccountId || item.TransferAccountId == command.AccountId))))
            || command.State == ClearingState.Reconciled || (command.State.HasValue && !Enum.IsDefined(command.State.Value)))
        {
            return new InvalidBudgetChange("Select transactions and a valid clearing state. Reconciled status is set through Reconcile.");
        }

        return plan with
        {
            Transactions = plan.Transactions.Select(item => command.TransactionIds.Contains(item.Id) ? UpdateState(item, command) : item).ToArray(),
        };
    }

    private static TransactionData UpdateState(TransactionData item, UpdateTransactionStates command)
    {
        var destination = command.AccountId == item.TransferAccountId && command.AccountId.HasValue;
        var state = command.State ?? (destination ? item.TransferState : item.State);
        return item with
        {
            State = destination ? item.State : state,
            TransferState = destination ? state : item.TransferState,
            NeedsApproval = !command.Approve && item.NeedsApproval,
        };
    }

    public static PlanSnapshot PostDue(PlanSnapshot plan, DateOnly today)
    {
        var transactions = plan.Transactions.ToList();
        foreach (var template in plan.Transactions.Where(item => item.Repeat != RepeatFrequency.None && item.Date <= today))
        {
            if (plan.Accounts.Any(item => (item.Id == template.AccountId || item.Id == template.TransferAccountId) && item.Closed))
            {
                continue;
            }

            var next = template;
            while (next.Date <= today)
            {
                if (!transactions.Any(item => item.SourceTemplateId == template.Id && item.Date == next.Date))
                {
                    transactions.Add(next with
                    {
                        Id = Guid.NewGuid(),
                        Repeat = RepeatFrequency.None,
                        SourceTemplateId = template.Id,
                        State = ClearingState.Uncleared,
                        TransferState = ClearingState.Uncleared,
                        NeedsApproval = true,
                        Splits = next.Splits.Select(item => item with { Id = Guid.NewGuid() }).ToArray(),
                    });
                }

                var occurrence = next.Occurrence + 1;
                next = next with { Occurrence = occurrence, Date = RecurrenceCalendar.DateAt(next.AnchorDate ?? template.Date, next.Repeat, occurrence) };
            }

            transactions[transactions.IndexOf(template)] = next;
        }

        return plan with { Transactions = transactions };
    }
}
