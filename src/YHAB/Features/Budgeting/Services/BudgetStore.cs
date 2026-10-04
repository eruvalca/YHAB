using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class BudgetStore(IDbContextFactory<ApplicationDbContext> contextFactory, BudgetClock clock, TimeProvider timeProvider, BudgetQueries queries)
{
    private static readonly JsonSerializerOptions _snapshotJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await database.Set<BudgetPlan>().AsNoTracking().Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.Name).Select(item => new PlanSummary(item.Id, item.Name, item.Notes, item.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> CreateAsync(string ownerId, CreatePlanRequest request, CancellationToken cancellationToken)
    {
        if (!CatalogChanges.ValidName(request.Name))
        {
            throw new BudgetRequestException(400, "Enter a plan name of 1–100 characters.");
        }

        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var plan = new BudgetPlan { Id = Guid.CreateVersion7(), OwnerId = ownerId, Name = request.Name.Trim(), CreatedOn = clock.Today };
        database.Add(plan);
        if (request.StarterCategories)
        {
            AddStarterCategories(database, plan.Id);
        }

        await database.SaveChangesAsync(cancellationToken);
        return plan.Id;
    }

    public async Task<PlanSnapshot?> ReadAsync(string ownerId, Guid planId, CancellationToken cancellationToken)
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            database.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            // The ledger is loaded with several queries. Keep them on one committed revision.
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            var plan = await database.Set<BudgetPlan>().SingleOrDefaultAsync(item => item.Id == planId && item.OwnerId == ownerId, cancellationToken);
            if (plan is null)
            {
                return null;
            }

            var snapshot = await BudgetSnapshotMapping.LoadAsync(database, plan, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return snapshot with { Today = clock.Today };
        });
    }

    public async Task<BudgetChangeOutcome> ExecuteAsync(string ownerId, Guid planId, PlanCommand command, CancellationToken cancellationToken)
        => (await ExecuteCoreAsync(ownerId, planId, command, background: false, cancellationToken)).Outcome;

    public async Task<bool> PostRecurringAsync(string ownerId, Guid planId, DateOnly through, CancellationToken cancellationToken)
    {
        var result = await ExecuteCoreAsync(ownerId, planId, new PostRecurring(0, through), background: true, cancellationToken);
        return result.Outcome.Match(_ => result.Committed,
            invalid => throw new InvalidOperationException(invalid.Message), _ => false, _ => false);
    }

    private async Task<Execution> ExecuteCoreAsync(string ownerId, Guid planId, PlanCommand command, bool background, CancellationToken cancellationToken)
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            // Background posting holds one plan row for one bounded batch. It reads
            // the latest revision after waiting; user commands still require their
            // supplied revision and cannot silently overwrite an intervening write.
            var plans = background ? database.Set<BudgetPlan>().FromSql($"""
                SELECT * FROM "BudgetPlans" WHERE "Id" = {planId} AND "OwnerId" = {ownerId} FOR UPDATE
                """) : database.Set<BudgetPlan>().Where(item => item.Id == planId && item.OwnerId == ownerId);
            var plan = await plans.SingleOrDefaultAsync(cancellationToken);
            if (plan is null)
            {
                return new Execution(new PlanNotFound(), false);
            }

            var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, _snapshotJson)));
            var receipt = await database.Set<BudgetReceipt>().FindAsync([planId, command.OperationId], cancellationToken);
            if (receipt is not null)
            {
                return string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal)
                    ? new Execution((await BudgetSnapshotMapping.LoadAsync(database, plan, cancellationToken, includeLedger: false, includeAllocations: false)) with { Today = clock.Today }, true)
                    : new Execution(new InvalidBudgetChange("This operation identifier was already used for a different change."), false);
            }

            if (!background && plan.Version != command.Version)
            {
                return new Execution(new PlanVersionConflict(), false);
            }

            var input = command is UndoChange or RedoChange
                ? await BudgetHistoryReader.ReadAsync(database, plan, command is UndoChange, cancellationToken)
                : new BudgetHistoryReader.Input(await BudgetSnapshotMapping.LoadAsync(database, plan, cancellationToken, command: command), null);
            var before = input.Current with { Today = clock.Today };
            var outcome = await PrepareAsync(database, ownerId, plan, before, command, input.Patch, cancellationToken);
            return await outcome.Match(
                async prepared =>
                {
                    var after = prepared.Snapshot;
                    var patch = prepared.Mutation;
                    if (background && patch.IsEmpty)
                    {
                        return new Execution(before, false);
                    }
                    try
                    {
                        // Claim the revision first. Competing writes fail before any ledger/history changes.
                        plan.Version++;
                        await database.SaveChangesAsync(cancellationToken);
                        plan.Name = after.Name;
                        plan.Notes = after.Notes;
                        await patch.Match(async ledger =>
                        {
                            await BudgetCheckpoints.InvalidateAsync(database, planId, plan.Version, ledger, cancellationToken);
                            BudgetSnapshotMapping.ApplyPatch(database, ledger);
                        }, payees => PayeePersistence.ApplyAsync(database, planId, payees, cancellationToken));
                        if (!patch.IsEmpty && command is not (UndoChange or RedoChange or PostRecurring))
                        {
                            await SaveHistoryAsync(database, plan, patch, command, cancellationToken);
                        }

                        database.Add(new BudgetReceipt { PlanId = planId, OperationId = command.OperationId, RequestHash = requestHash, Version = plan.Version });

                        await database.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return new Execution(after with { Version = plan.Version }, true);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        return new Execution(new PlanVersionConflict(), false);
                    }
                },
                invalid => Task.FromResult(new Execution(invalid, false)),
                missing => Task.FromResult(new Execution(missing, false)),
                conflict => Task.FromResult(new Execution(conflict, false)));
        });
    }

    private sealed record Execution(BudgetChangeOutcome Outcome, bool Committed);

    private async Task<BudgetPreparationOutcome> PrepareAsync(ApplicationDbContext database, string owner, BudgetPlan plan,
        PlanSnapshot before, PlanCommand command, BudgetMutation? history, CancellationToken token)
    {
        if (command is RenamePayee rename) { return await PayeePersistence.PrepareAsync(database, before, rename, token); }
        var reverse = command is UndoChange;
        if (reverse || command is RedoChange)
        {
            if (history is null)
            {
                return new InvalidBudgetChange(reverse ? "There is nothing to undo." : "There is nothing to redo.");
            }
            return await history.Match(
                ledger => Task.FromResult(PrepareLedger(plan, before, command, Restore(plan, before, command, ledger))),
                async payees =>
                {
                    var patch = reverse ? payees.Reverse() : payees;
                    if (!await PayeePersistence.CanApplyAsync(database, plan.Id, patch, token))
                    {
                        return (BudgetPreparationOutcome)new InvalidBudgetChange("A renamed transaction was removed or its payee changed. Review those entries before undoing or redoing it.");
                    }
                    plan.HistoryCursor = HistoryPolicy.RestoredCursor(plan.HistoryCursor, !reverse);
                    return new PreparedBudgetChange(before, patch);
                });
        }
        return PrepareLedger(plan, before, command, await ApplyAsync(database, owner, plan, before, command, token));
    }

    private static BudgetPreparationOutcome PrepareLedger(BudgetPlan plan, PlanSnapshot before, PlanCommand command, BudgetChangeOutcome outcome)
        => outcome.Match(after =>
        {
            // The plan revision serializes sequence allocation. Restored records retain their original sequence.
            var ordered = TransactionOrdering.Assign(before.Transactions, after.Transactions, plan.NextSequence, command is UndoChange or RedoChange);
            plan.NextSequence = ordered.LastSequence;
            after = after with { Transactions = ordered.Transactions };
            return (BudgetPreparationOutcome)new PreparedBudgetChange(after, LedgerPatch.Between(before, after));
        }, invalid => invalid, missing => missing, conflict => conflict);

    private async Task<BudgetChangeOutcome> ApplyAsync(ApplicationDbContext database, string owner, BudgetPlan plan,
        PlanSnapshot before, PlanCommand command, CancellationToken token)
    {
        DateOnly? month = command switch { MoveMoney move => move.Month, AutoAssign auto => auto.Month, _ => null };
        BudgetMonth? projection = null;
        if (month is { Day: 1 } date && CatalogChanges.ValidDate(date))
        {
            try
            {
                projection = await queries.MonthAsync(owner, plan.Id, date, plan.Version, token);
            }
            catch (BudgetRequestException exception) when (exception.Status == 409) { return new PlanVersionConflict(); }
            catch (BudgetRequestException exception) when (exception.Status == 404) { return new PlanNotFound(); }
        }
        var ids = CommandIds.Allocate(before, command);
        if (command is SaveAccount { Account: { } account } save)
        {
            var facts = await AccountLedgerQueries.ReadAsync(database, plan.Id, account.Id, clock.Today, token);
            return CatalogChanges.Save(ids, before, save, clock.Today, facts);
        }
        if (command is ReconcileAccount reconcile)
        {
            var facts = await AccountLedgerQueries.ReadAsync(database, plan.Id, reconcile.AccountId, reconcile.Date, token);
            return MoneyChanges.Reconcile(ids, before, reconcile, clock.Today, facts.ClearedMovement);
        }
        var outcome = PlanCommandHandler.Apply(ids, before, command, clock.Today, projection);
        if (command is not (PostRecurring or SaveTransaction { Transaction.Repeat: not RepeatFrequency.None }))
        {
            return outcome;
        }

        return await outcome.Match(async proposed =>
        {
            var posted = await RecurringOccurrences.ExistingAsync(database, before, proposed, token);
            return posted.Count == 0 ? proposed : PlanCommandHandler.Apply(ids, before, command, clock.Today, projection, posted);
        }, invalid => Task.FromResult<BudgetChangeOutcome>(invalid),
            missing => Task.FromResult<BudgetChangeOutcome>(missing), conflict => Task.FromResult<BudgetChangeOutcome>(conflict));
    }

    private static BudgetChangeOutcome Restore(BudgetPlan plan, PlanSnapshot current, PlanCommand command, LedgerPatch patch)
    {
        if (!patch.TryApply(current, command is UndoChange, out var restored))
        {
            return new InvalidBudgetChange("This change conflicts with later automatic entries. Review those entries before undoing or redoing it.");
        }
        plan.HistoryCursor = HistoryPolicy.RestoredCursor(plan.HistoryCursor, command is RedoChange);
        return restored;
    }

    private async Task SaveHistoryAsync(ApplicationDbContext database, BudgetPlan plan, BudgetMutation patch,
        PlanCommand command, CancellationToken cancellationToken)
    {
        var discarded = await database.Set<BudgetHistory>().Where(item => item.PlanId == plan.Id
            && (item.Position > plan.HistoryCursor || item.Position <= plan.HistoryCursor - HistoryPolicy.Capacity + 1)).ToListAsync(cancellationToken);
        database.RemoveRange(discarded);
        // Release a redo position before inserting a replacement with the same unique key.
        await database.SaveChangesAsync(cancellationToken);
        plan.HistoryCursor++;
        var serialized = BudgetHistoryCodec.Serialize(patch);
        database.Add(new BudgetHistory
        {
            Id = Guid.CreateVersion7(),
            PlanId = plan.Id,
            Position = plan.HistoryCursor,
            Description = PlanCommandHandler.Describe(command),
            Timestamp = timeProvider.GetUtcNow(),
            Before = serialized.Before,
            After = serialized.After,
        });
    }

    private static void AddStarterCategories(ApplicationDbContext database, Guid planId)
    {
        var groups = new (string Name, string[] Categories)[]
        {
            ("Essentials", ["Rent & mortgage", "Groceries", "Utilities", "Transport", "Health"]),
            ("Everyday living", ["Dining out", "Shopping", "Entertainment", "Subscriptions"]),
            ("Looking ahead", ["Emergency fund", "Travel", "Home & car repairs", "Gifts", "Things I forgot"]),
        };
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = new BudgetGroup { Id = Guid.CreateVersion7(), PlanId = planId, Name = groups[groupIndex].Name, SortOrder = groupIndex };
            database.Add(group);
            for (var categoryIndex = 0; categoryIndex < groups[groupIndex].Categories.Length; categoryIndex++)
            {
                database.Add(new BudgetCategory
                {
                    Id = Guid.CreateVersion7(),
                    PlanId = planId,
                    GroupId = group.Id,
                    Name = groups[groupIndex].Categories[categoryIndex],
                    SortOrder = categoryIndex,
                });
            }
        }
    }
}
