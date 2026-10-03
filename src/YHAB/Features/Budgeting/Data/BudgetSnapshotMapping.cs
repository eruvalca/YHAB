using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using YHAB.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal static class BudgetSnapshotMapping
{
    public static async Task<PlanSnapshot> LoadAsync(ApplicationDbContext database, BudgetPlan plan, CancellationToken cancellationToken)
    {
        var accounts = await database.Set<BudgetAccount>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var groups = await database.Set<BudgetGroup>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var categories = await database.Set<BudgetCategory>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var allocations = await database.Set<BudgetAllocation>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var transactions = await database.Set<BudgetTransaction>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var splits = await database.Set<BudgetSplit>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var splitsByTransaction = splits.ToLookup(item => item.TransactionId);
        var history = await database.Set<BudgetHistory>().Where(item => item.PlanId == plan.Id)
            .OrderByDescending(item => item.Position)
            .Select(item => new { item.Position, item.Description, item.Timestamp }).ToListAsync(cancellationToken);
        return new(plan.Id, plan.Name, plan.Notes, plan.CreatedOn, plan.Version,
            accounts.Select(ToData).ToArray(), groups.OrderBy(item => item.SortOrder).Select(ToData).ToArray(),
            categories.OrderBy(item => item.SortOrder).Select(ToData).ToArray(), allocations.Select(ToData).ToArray(),
            transactions.Select(item => ToData(item, splitsByTransaction[item.Id].Select(ToData).ToArray())).ToArray(),
            history.Any(item => item.Position == plan.HistoryCursor), history.Any(item => item.Position == plan.HistoryCursor + 1),
            history.Where(item => item.Position <= plan.HistoryCursor).Take(10).Select(item => new ChangeData(item.Description, item.Timestamp)).ToArray());
    }

    public static void Apply(ApplicationDbContext database, PlanSnapshot snapshot)
    {
        var removed = new List<EntityEntry>();
        Sync(database, snapshot.Accounts.Select(item => ToEntity(item, snapshot.Id)), item => item.Id, removed);
        Sync(database, snapshot.Groups.Select(item => ToEntity(item, snapshot.Id)), item => item.Id, removed);
        Sync(database, snapshot.Categories.Select(item => ToEntity(item, snapshot.Id)), item => item.Id, removed);
        Sync(database, snapshot.Allocations.Select(item => ToEntity(item, snapshot.Id)), item => (item.CategoryId, item.Month), removed);
        Sync(database, snapshot.Transactions.Select(item => ToEntity(item, snapshot.Id)), item => item.Id, removed);
        Sync(database, snapshot.Transactions.SelectMany(entry => entry.Splits.Select(split => ToEntity(split, snapshot.Id, entry.Id))), item => item.Id, removed);

        // Reparent retained dependents first, then remove children before their principals.
        // EF enforces required Restrict relationships as soon as an entity is marked deleted.
        for (var index = removed.Count - 1; index >= 0; index--)
        {
            removed[index].State = EntityState.Deleted;
        }
    }

    private static void Sync<TEntity, TKey>(ApplicationDbContext database, IEnumerable<TEntity> desired, Func<TEntity, TKey> key, List<EntityEntry> removed)
        where TEntity : class
        where TKey : notnull
    {
        var existing = database.ChangeTracker.Entries<TEntity>().ToDictionary(entry => key(entry.Entity));
        foreach (var entity in desired)
        {
            if (existing.Remove(key(entity), out var entry))
            {
                entry.CurrentValues.SetValues(entity);
            }
            else
            {
                database.Set<TEntity>().Add(entity);
            }
        }

        removed.AddRange(existing.Values);
    }

    private static AccountData ToData(BudgetAccount item)
        => new(item.Id, item.Name, item.Kind, item.OpeningBalance, item.OpenedOn, item.Closed, item.Notes, item.InterestRate, item.MinimumPayment);

    private static GroupData ToData(BudgetGroup item)
        => new(item.Id, item.Name, item.SortOrder, item.Hidden);

    private static AllocationData ToData(BudgetAllocation item)
        => new(item.CategoryId, item.Month, item.Amount, item.Snoozed);

    private static SplitData ToData(BudgetSplit item)
        => new(item.Id, item.CategoryId, item.Amount, item.Memo);

    private static TransactionData ToData(BudgetTransaction item, IReadOnlyList<SplitData> splits)
        => new(item.Id, item.AccountId, item.Date, item.Payee, item.Memo, item.Amount, item.TransferAccountId, item.State, item.TransferState, item.NeedsApproval, item.Flag, splits, item.Repeat, item.AnchorDate, item.Occurrence, item.SourceTemplateId, item.ScheduledDate);

    private static CategoryData ToData(BudgetCategory item)
        => new(item.Id, item.GroupId, item.Name, item.Notes, item.SortOrder, item.Hidden, item.CreditAccountId, item.TargetKind is { } kind ? new TargetData(kind, item.TargetCadence, item.TargetAmount, item.TargetStartMonth, item.TargetDueDate, item.TargetRepeatMonths, item.TargetWeekday) : null);

    private static BudgetAccount ToEntity(AccountData item, Guid planId) => new()
    {
        PlanId = planId,
        Id = item.Id,
        Name = item.Name,
        Kind = item.Kind,
        OpeningBalance = item.OpeningBalance,
        OpenedOn = item.OpenedOn,
        Closed = item.Closed,
        Notes = item.Notes,
        InterestRate = item.InterestRate,
        MinimumPayment = item.MinimumPayment,
    };

    private static BudgetGroup ToEntity(GroupData item, Guid planId) => new()
    {
        PlanId = planId,
        Id = item.Id,
        Name = item.Name,
        SortOrder = item.SortOrder,
        Hidden = item.Hidden,
    };

    private static BudgetAllocation ToEntity(AllocationData item, Guid planId) => new()
    {
        PlanId = planId,
        CategoryId = item.CategoryId,
        Month = item.Month,
        Amount = item.Amount,
        Snoozed = item.Snoozed,
    };

    private static BudgetSplit ToEntity(SplitData item, Guid planId, Guid transactionId) => new()
    {
        PlanId = planId,
        Id = item.Id,
        CategoryId = item.CategoryId,
        Amount = item.Amount,
        Memo = item.Memo,
        TransactionId = transactionId,
    };

    private static BudgetTransaction ToEntity(TransactionData item, Guid planId) => new()
    {
        PlanId = planId,
        Id = item.Id,
        AccountId = item.AccountId,
        Date = item.Date,
        Payee = item.Payee,
        Memo = item.Memo,
        Amount = item.Amount,
        TransferAccountId = item.TransferAccountId,
        State = item.State,
        TransferState = item.TransferState,
        NeedsApproval = item.NeedsApproval,
        Flag = item.Flag,
        Repeat = item.Repeat,
        AnchorDate = item.AnchorDate,
        Occurrence = item.Occurrence,
        SourceTemplateId = item.SourceTemplateId,
        // History written before ScheduledDate existed still needs a stable occurrence key.
        ScheduledDate = item.ScheduledDate ?? (item.SourceTemplateId.HasValue ? item.Date : null),
    };

    private static BudgetCategory ToEntity(CategoryData item, Guid planId) => new()
    {
        PlanId = planId,
        Id = item.Id,
        GroupId = item.GroupId,
        Name = item.Name,
        Notes = item.Notes,
        SortOrder = item.SortOrder,
        Hidden = item.Hidden,
        CreditAccountId = item.CreditAccountId,
        TargetKind = item.Target?.Kind,
        TargetCadence = item.Target?.Cadence ?? TargetCadence.Monthly,
        TargetAmount = item.Target?.Amount ?? 0,
        TargetStartMonth = item.Target?.StartMonth ?? default,
        TargetDueDate = item.Target?.DueDate,
        TargetRepeatMonths = item.Target?.RepeatEveryMonths ?? 0,
        TargetWeekday = item.Target?.Weekday ?? DayOfWeek.Friday,
    };
}
