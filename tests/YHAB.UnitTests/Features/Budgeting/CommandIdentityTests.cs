using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CommandIdentityTests
{
    [Theory]
    [InlineData("account")]
    [InlineData("group")]
    [InlineData("category")]
    [InlineData("transaction")]
    [InlineData("recurring")]
    [InlineData("reconcile")]
    [InlineData("assign")]
    public void SuppliedIdentitiesMakeEveryCreatingCommandReplayExactly(string operation)
    {
        var plan = Create();
        var entry = Entry(plan, 0, -10) with { Id = Guid.Empty, Splits = [new(Guid.Empty, plan.Categories[0].Id, -10, "")] };
        PlanCommand command = operation switch
        {
            "account" => new SaveAccount(0, plan.Accounts[1] with { Id = Guid.Empty }),
            "group" => new SaveGroup(0, new(Guid.Empty, "New group", 1)),
            "category" => new SaveCategory(0, plan.Categories[0] with { Id = Guid.Empty }),
            "transaction" => new SaveTransaction(0, entry),
            "recurring" => new SaveTransaction(0, entry with { Repeat = RepeatFrequency.Daily }),
            "reconcile" => new ReconcileAccount(0, plan.Accounts[0].Id, plan.Today, 900, true),
            _ => new AssignMoney(0, plan.Categories[0].Id, January, 20),
        };
        var ids = CommandIds.Allocate(plan, command);
        ids.Values.ShouldAllBe(id => id.Version == 7);
        ids.Values.Distinct().Count().ShouldBe(ids.Values.Length);
        var input = JsonSerializer.Serialize(plan);
        var identityInput = ids.Values.ToArray();
        var first = PlanCommandHandler.Apply(ids, plan, command, plan.Today).AsT0;
        var second = PlanCommandHandler.Apply(ids, plan, command, plan.Today).AsT0;
        JsonSerializer.Serialize(second).ShouldBe(JsonSerializer.Serialize(first));
        JsonSerializer.Serialize(plan).ShouldBe(input);
        ids.Values.ShouldBe(identityInput);
        // Assert that allocation data is actually used, rather than a coincidentally stable result.
        var created = first.Accounts.Select(item => item.Id).Concat(first.Groups.Select(item => item.Id))
            .Concat(first.Categories.Select(item => item.Id)).Concat(first.Transactions.Select(item => item.Id))
            .Except(plan.Accounts.Select(item => item.Id).Concat(plan.Groups.Select(item => item.Id)).Concat(plan.Categories.Select(item => item.Id)));
        if (string.Equals(operation, "assign", StringComparison.Ordinal)) { ids.Values.ShouldBeEmpty(); first.Allocations.Single().Amount.ShouldBe(20); }
        else { created.ShouldNotBeEmpty(); created.ShouldAllBe(id => ids.Values.Contains(id)); }
    }

    [Fact]
    public void MalformedCommandsReachValidationWithoutUnboundedIdentityAllocation()
    {
        var plan = Create();
        foreach (var entry in new TransactionData?[] { null, Entry(plan, 0, -1) with { Splits = null!, Repeat = RepeatFrequency.Daily } })
        {
            var command = new SaveTransaction(0, entry!);
            var ids = CommandIds.Allocate(plan, command);
            ids.Values.Length.ShouldBeLessThanOrEqualTo(129);
            PlanCommandHandler.Apply(ids, plan, command, plan.Today).IsT1.ShouldBeTrue();
        }
    }

    [Fact]
    public void MaximumRecurringBatchHasDistinctSuppliedTransactionAndSplitIdentities()
    {
        var plan = Create() with { Today = January.AddDays(127) };
        var entry = Entry(plan, 0, -100, date: January) with
        {
            Id = Guid.Empty,
            Repeat = RepeatFrequency.Daily,
            Splits = Enumerable.Range(0, 100).Select(_ => new SplitData(Guid.Empty, plan.Categories[0].Id, -1, "")).ToArray(),
        };
        var command = new SaveTransaction(0, entry);
        var ids = CommandIds.Allocate(plan, command);
        var result = PlanCommandHandler.Apply(ids, plan, command, plan.Today).AsT0;
        result.Transactions.Count.ShouldBe(129);
        var used = result.Transactions.SelectMany(item => item.Splits.Select(split => split.Id).Prepend(item.Id)).ToArray();
        used.Length.ShouldBe(13029);
        used.Distinct().Count().ShouldBe(used.Length);
        used.ShouldBe(ids.Values);
        result.Transactions.Count(item => item.NeedsApproval).ShouldBe(128);
        result.Transactions.Single(item => item.Repeat != RepeatFrequency.None).Date.ShouldBe(January.AddDays(128));
        var posting = new PostRecurring(0, plan.Today);
        var postingIds = CommandIds.Allocate(result, posting);
        PlanCommandHandler.Apply(postingIds, result, posting, plan.Today).AsT0.Transactions.ShouldBe(result.Transactions);
    }
}
