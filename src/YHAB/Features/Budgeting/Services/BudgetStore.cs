using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class BudgetStore(IDbContextFactory<ApplicationDbContext> contextFactory, BudgetClock clock, TimeProvider timeProvider)
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
        var plan = new BudgetPlan { Id = Guid.NewGuid(), OwnerId = ownerId, Name = request.Name.Trim(), CreatedOn = clock.Today };
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
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var plan = await database.Set<BudgetPlan>().SingleOrDefaultAsync(item => item.Id == planId && item.OwnerId == ownerId, cancellationToken);
            if (plan is null)
            {
                return (BudgetChangeOutcome)new PlanNotFound();
            }

            if (plan.Version != command.Version)
            {
                return new PlanVersionConflict();
            }

            var before = (await BudgetSnapshotMapping.LoadAsync(database, plan, cancellationToken)) with { Today = clock.Today };
            var outcome = command is UndoChange or RedoChange
                ? await RestoreAsync(database, plan, command, cancellationToken)
                : PlanCommandHandler.Apply(before, command, clock.Today);
            return await outcome.Match(
                async after =>
                {
                    try
                    {
                        // Claim the revision first. Competing writes fail before any ledger/history changes.
                        plan.Version++;
                        await database.SaveChangesAsync(cancellationToken);
                        plan.Name = after.Name;
                        plan.Notes = after.Notes;
                        BudgetSnapshotMapping.Apply(database, after);
                        if (command is not (UndoChange or RedoChange))
                        {
                            await SaveHistoryAsync(database, plan, before, after, command, cancellationToken);
                        }

                        await database.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return (BudgetChangeOutcome)(after with { Version = plan.Version });
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        return new PlanVersionConflict();
                    }
                },
                invalid => Task.FromResult<BudgetChangeOutcome>(invalid),
                missing => Task.FromResult<BudgetChangeOutcome>(missing),
                conflict => Task.FromResult<BudgetChangeOutcome>(conflict));
        });
    }

    private static async Task<BudgetChangeOutcome> RestoreAsync(ApplicationDbContext database, BudgetPlan plan,
        PlanCommand command, CancellationToken cancellationToken)
    {
        var position = command is UndoChange ? plan.HistoryCursor : plan.HistoryCursor + 1;
        var history = await database.Set<BudgetHistory>().SingleOrDefaultAsync(item => item.PlanId == plan.Id && item.Position == position, cancellationToken);
        if (history is null)
        {
            return new InvalidBudgetChange(command is UndoChange ? "There is nothing to undo." : "There is nothing to redo.");
        }

        var restored = JsonSerializer.Deserialize<PlanSnapshot>(command is UndoChange ? history.Before : history.After, _snapshotJson)
            ?? throw new InvalidOperationException("The saved plan history could not be read.");
        plan.HistoryCursor += command is UndoChange ? -1 : 1;
        return restored;
    }

    private async Task SaveHistoryAsync(ApplicationDbContext database, BudgetPlan plan, PlanSnapshot before,
        PlanSnapshot after, PlanCommand command, CancellationToken cancellationToken)
    {
        var discarded = await database.Set<BudgetHistory>().Where(item => item.PlanId == plan.Id
            && (item.Position > plan.HistoryCursor || item.Position <= plan.HistoryCursor - 49)).ToListAsync(cancellationToken);
        database.RemoveRange(discarded);
        // Release a redo position before inserting a replacement with the same unique key.
        await database.SaveChangesAsync(cancellationToken);
        plan.HistoryCursor++;
        database.Add(new BudgetHistory
        {
            Id = Guid.NewGuid(),
            PlanId = plan.Id,
            Position = plan.HistoryCursor,
            Description = PlanCommandHandler.Describe(command),
            Timestamp = timeProvider.GetUtcNow(),
            Before = JsonSerializer.Serialize(before, _snapshotJson),
            After = JsonSerializer.Serialize(after, _snapshotJson),
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
            var group = new BudgetGroup { Id = Guid.NewGuid(), PlanId = planId, Name = groups[groupIndex].Name, SortOrder = groupIndex };
            database.Add(group);
            for (var categoryIndex = 0; categoryIndex < groups[groupIndex].Categories.Length; categoryIndex++)
            {
                database.Add(new BudgetCategory
                {
                    Id = Guid.NewGuid(),
                    PlanId = planId,
                    GroupId = group.Id,
                    Name = groups[groupIndex].Categories[categoryIndex],
                    SortOrder = categoryIndex,
                });
            }
        }
    }
}
