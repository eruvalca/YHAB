using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PatchPersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SparsePatchPreservesOtherTrackedRowsAndDetectionSettingAsync(bool detectChanges)
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await BudgetDatabase.CreateAsync(token);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 100, 2, 3, 4, token);
        await using var context = await database.Factory.CreateDbContextAsync(token);
        var plan = await context.Set<BudgetPlan>().SingleAsync(item => item.Id == fixture.Id, token);
        var before = await BudgetSnapshotMapping.LoadAsync(context, plan, token);
        var allocation = before.Allocations[0];
        var deletedAllocation = before.Allocations[1];
        var entry = before.Transactions[0];
        var deletedEntry = before.Transactions[1];
        var addedEntry = entry with { Id = Guid.CreateVersion7(), Sequence = 101, Splits = entry.Splits.Select(item => item with { Id = Guid.CreateVersion7() }).ToArray() };
        var after = before with
        {
            Allocations = [.. before.Allocations.Where(item => item != allocation && item != deletedAllocation),
                allocation with { Amount = 25 }, new(allocation.CategoryId, fixture.Month.AddMonths(1), 77)],
            Transactions = [.. before.Transactions.Where(item => item.Id != entry.Id && item.Id != deletedEntry.Id),
                entry with { Payee = "Edited", Splits = entry.Splits.Select(item => item with { Memo = "Changed split" }).ToArray() }, addedEntry],
        };
        context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        BudgetSnapshotMapping.ApplyPatch(context, LedgerPatch.Between(before, after));
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBe(detectChanges);
        await context.SaveChangesAsync(token);
        var actual = (await database.Store.ReadAsync("owner-a", fixture.Id, token)).ShouldNotBeNull();
        actual.Accounts.OrderBy(item => item.Id).ShouldBe(before.Accounts.OrderBy(item => item.Id));
        actual.Groups.ShouldBe(before.Groups);
        actual.Categories.ShouldBe(before.Categories);
        actual.Allocations.OrderBy(item => item.CategoryId).ThenBy(item => item.Month)
            .ShouldBe(after.Allocations.OrderBy(item => item.CategoryId).ThenBy(item => item.Month));
        JsonSerializer.Serialize(actual.Transactions.OrderBy(item => item.Id))
            .ShouldBe(JsonSerializer.Serialize(after.Transactions.OrderBy(item => item.Id)));
        (await context.Set<BudgetSplit>().AsNoTracking().CountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe(100);
    }
}
