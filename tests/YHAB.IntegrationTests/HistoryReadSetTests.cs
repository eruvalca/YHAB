using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HistoryReadSetTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("transfer")]
    [InlineData("category")]
    [InlineData("allocation")]
    public async Task UndoLoadsLaterDependentsAndRejectsDeletingTheirPrincipalAsync(string dependent)
    {
        var token = TestContext.Current.CancellationToken;
        var probe = new DatabaseProbe();
        await using var database = await BudgetDatabase.CreateAsync(token, interceptor: probe);
        var fixture = await LedgerScaleFixture.CreateAsync(database, 1000, 2, 3, 24, token);
        var accountAdded = dependent is "source" or "transfer";
        var addedId = Guid.CreateVersion7();
        PlanCommand create = accountAdded
            ? new SaveAccount(0, new(addedId, "Later account", AccountKind.Checking, 0, fixture.Start, false, ""))
            : new SaveCategory(0, fixture.Categories[0] with { Id = addedId, Name = "Later category" });
        (await database.Store.ExecuteAsync("owner-a", fixture.Id, create, token)).IsT0.ShouldBeTrue();
        await using var context = await database.Factory.CreateDbContextAsync(token);
        // Simulate a subsequent automatic write that does not advance user history.
        // It must prevent undo from removing a principal it now references.
        if (dependent is "allocation")
        {
            context.Add(new BudgetAllocation { PlanId = fixture.Id, CategoryId = addedId, Month = fixture.Month, Amount = 10 });
        }
        else
        {
            var transactionId = Guid.CreateVersion7();
            context.Add(new BudgetTransaction
            {
                PlanId = fixture.Id,
                Id = transactionId,
                AccountId = dependent is "source" ? addedId : fixture.Accounts[0].Id,
                TransferAccountId = dependent is "transfer" ? addedId : null,
                Date = fixture.Month,
                Amount = -10,
                Sequence = 1001,
            });
            if (dependent is "category")
            {
                context.Add(new BudgetSplit { PlanId = fixture.Id, Id = Guid.CreateVersion7(), TransactionId = transactionId, CategoryId = addedId, Amount = -10 });
            }
        }
        await context.SaveChangesAsync(token);
        await context.Set<BudgetPlan>().Where(item => item.Id == fixture.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Version, 2).SetProperty(item => item.NextSequence, 1001), token);
        probe.Start();
        var result = await database.Store.ExecuteAsync("owner-a", fixture.Id, new UndoChange(2), token);
        probe.Stop();
        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("later automatic entries");
        probe.Materialized.GetValueOrDefault(nameof(BudgetTransaction)).ShouldBe(dependent is "allocation" ? 0 : 1);
        probe.Materialized.GetValueOrDefault(nameof(BudgetAllocation)).ShouldBe(dependent is "allocation" ? 1 : 0);
        (await database.Queries.RevisionAsync("owner-a", fixture.Id, token)).ShouldBe(2);
        (await context.Set<BudgetReceipt>().CountAsync(item => item.PlanId == fixture.Id, token)).ShouldBe(1);
        (await context.Set<BudgetPlan>().Where(item => item.Id == fixture.Id).Select(item => item.HistoryCursor).SingleAsync(token)).ShouldBe(1);
    }
}
