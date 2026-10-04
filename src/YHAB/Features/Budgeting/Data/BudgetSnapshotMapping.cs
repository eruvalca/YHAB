using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using YHAB.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal static class BudgetSnapshotMapping
{
    public static async Task<PlanSnapshot> LoadAsync(ApplicationDbContext database, BudgetPlan plan, CancellationToken cancellationToken, bool includeLedger = true, PlanCommand? command = null, bool includeAllocations = true)
    {
        includeLedger &= LedgerLoadPolicy.NeedsLedger(command);
        var accounts = await database.Set<BudgetAccount>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var groups = await database.Set<BudgetGroup>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var categories = await database.Set<BudgetCategory>().Where(item => item.PlanId == plan.Id).ToListAsync(cancellationToken);
        var allocations = includeAllocations ? await AllocationLoadPolicy.LoadAsync(database, plan.Id, command, cancellationToken) : [];
        var ledger = includeLedger ? await LedgerLoadPolicy.SelectAsync(database, plan.Id, command, cancellationToken) : database.Set<BudgetTransaction>().Where(_ => false);
        var transactions = includeLedger ? await ledger.ToListAsync(cancellationToken) : [];
        var ids = transactions.Select(item => item.Id).ToArray();
        var splits = ids.Length != 0 ? await database.Set<BudgetSplit>().Where(item => item.PlanId == plan.Id && ids.Contains(item.TransactionId)).ToListAsync(cancellationToken) : [];
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

    public static async Task<IReadOnlyList<TransactionData>> ReadLedgerAsync(ApplicationDbContext database, Guid planId,
        DateOnly from, DateOnly through, CancellationToken token)
    {
        var query = database.Set<BudgetTransaction>().Where(item => item.PlanId == planId && item.Repeat == RepeatFrequency.None && item.Date >= from && item.Date <= through);
        var entries = await query.ToArrayAsync(token);
        var splits = await database.Set<BudgetSplit>().Where(item => item.PlanId == planId && query.Any(entry => entry.Id == item.TransactionId)).ToArrayAsync(token);
        var grouped = splits.ToLookup(item => item.TransactionId);
        return entries.Select(item => ToData(item, grouped[item.Id].Select(ToData).ToArray())).ToArray();
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

    public static void ApplyPatch(ApplicationDbContext database, LedgerPatch patch)
    {
        var detectChanges = database.ChangeTracker.AutoDetectChangesEnabled;
        database.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var before = patch.Before;
            var after = patch.After;
            var removed = new List<EntityEntry>();
            SyncPatch(database, before.Accounts.Select(item => ToEntity(item, before.Id)), after.Accounts.Select(item => ToEntity(item, after.Id)), item => item.Id, removed);
            SyncPatch(database, before.Groups.Select(item => ToEntity(item, before.Id)), after.Groups.Select(item => ToEntity(item, after.Id)), item => item.Id, removed);
            SyncPatch(database, before.Categories.Select(item => ToEntity(item, before.Id)), after.Categories.Select(item => ToEntity(item, after.Id)), item => item.Id, removed);
            SyncPatch(database, before.Allocations.Select(item => ToEntity(item, before.Id)), after.Allocations.Select(item => ToEntity(item, after.Id)), item => (item.CategoryId, item.Month), removed);
            SyncPatch(database, before.Transactions.Select(item => ToEntity(item, before.Id)), after.Transactions.Select(item => ToEntity(item, after.Id)), item => item.Id, removed);
            SyncPatch(database, before.Transactions.SelectMany(entry => entry.Splits.Select(split => ToEntity(split, before.Id, entry.Id))),
                after.Transactions.SelectMany(entry => entry.Splits.Select(split => ToEntity(split, after.Id, entry.Id))), item => item.Id, removed);
            for (var index = removed.Count - 1; index >= 0; index--) { removed[index].State = EntityState.Deleted; }
        }
        finally
        {
            database.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        }
    }

    private static void SyncPatch<TEntity, TKey>(ApplicationDbContext database, IEnumerable<TEntity> before, IEnumerable<TEntity> after,
        Func<TEntity, TKey> key, List<EntityEntry> removed) where TEntity : class where TKey : notnull
    {
        var previous = before.ToDictionary(key);
        var desired = after.ToArray();
        if (previous.Count + desired.Length == 0) { return; }
        var tracked = database.ChangeTracker.Entries<TEntity>().ToDictionary(entry => key(entry.Entity));
        foreach (var entity in desired)
        {
            var id = key(entity);
            if (previous.Remove(id)) { tracked[id].CurrentValues.SetValues(entity); }
            else { database.Set<TEntity>().Add(entity); }
        }
        // Only a record explicitly removed by the patch can be deleted. Other
        // tracked entities may have been read for validation or calculation.
        removed.AddRange(previous.Keys.Select(id => tracked[id]));
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

    internal static SplitData ToData(BudgetSplit item)
        => new(item.Id, item.CategoryId, item.Amount, item.Memo);

    internal static TransactionData ToData(BudgetTransaction item, IReadOnlyList<SplitData> splits)
        => new(item.Id, item.AccountId, item.Date, item.Payee, item.Memo, item.Amount, item.TransferAccountId, item.State, item.TransferState, item.NeedsApproval, item.Flag, splits, item.Repeat, item.AnchorDate, item.Occurrence, item.SourceTemplateId, item.ScheduledDate, item.Sequence);

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
        Sequence = item.Sequence,
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
