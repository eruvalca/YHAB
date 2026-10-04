using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Models;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CheckpointPolicyTests
{
    [Fact]
    public void PresentationTargetsClearingAndSnoozingPreserveFinancialOpeningsAndInputs()
    {
        var original = Assigned(Create(), 100);
        original = original with { Transactions = [Entry(original, 0, -20) with { Sequence = 7 }] };
        var entry = original.Transactions[0];
        var updated = original with
        {
            Name = "Renamed",
            Notes = "Plan notes",
            Accounts = original.Accounts.Select(item => item with { Name = "New name", Notes = "Account notes", Closed = true, InterestRate = 5, MinimumPayment = 50 }).Reverse().ToArray(),
            Categories = original.Categories.Select(item => item with
            {
                Name = "New category",
                Notes = "Category notes",
                Hidden = true,
                SortOrder = 9,
                Target = new(TargetKind.Refill, TargetCadence.Yearly, 500, January, January.AddMonths(11))
            }).Reverse().ToArray(),
            Groups = [original.Groups[0] with { Name = "New group", SortOrder = 8, Hidden = true }],
            Allocations = [original.Allocations[0] with { Snoozed = true }],
            Transactions = [entry with { Payee = "Other payee", Memo = "Memo", State = ClearingState.Reconciled, TransferState = ClearingState.Cleared,
                NeedsApproval = true, Flag = "blue", SourceTemplateId = Guid.NewGuid(), ScheduledDate = January, AnchorDate = January, Occurrence = 4,
                Splits = entry.Splits.Select(item => item with { Memo = "Split memo" }).Reverse().ToArray() }],
        };
        var patch = LedgerPatch.Between(original, updated);
        var serialized = JsonSerializer.Serialize(patch);
        CheckpointPolicy.For(patch).Value.ShouldBeOfType<PreserveCheckpoints>();
        CheckpointPolicy.For(LedgerPatch.Between(updated, original)).Value.ShouldBeOfType<PreserveCheckpoints>();
        JsonSerializer.Serialize(patch).ShouldBe(serialized);
        CheckpointPolicy.For(LedgerPatch.Between(original, original)).Value.ShouldBeOfType<PreserveCheckpoints>();
    }

    [Theory]
    [InlineData("opening")]
    [InlineData("date")]
    [InlineData("kind")]
    [InlineData("add-account")]
    [InlineData("remove-account")]
    [InlineData("payment")]
    [InlineData("add-category")]
    [InlineData("remove-category")]
    public void AccountFinancialOrCategoryIdentityChangesResetAllOpenings(string change)
    {
        var original = Create();
        var account = original.Accounts[0];
        var category = original.Categories[0];
        var updated = change switch
        {
            "opening" => original with { Accounts = [account with { OpeningBalance = 500 }, original.Accounts[1]] },
            "date" => original with { Accounts = [account with { OpenedOn = January.AddDays(1) }, original.Accounts[1]] },
            "kind" => original with { Accounts = [account with { Kind = AccountKind.Asset }, original.Accounts[1]] },
            "add-account" => original with { Accounts = [.. original.Accounts, account with { Id = Guid.NewGuid() }] },
            "remove-account" => original with { Accounts = [original.Accounts[1]] },
            "payment" => original with { Categories = [category with { CreditAccountId = account.Id }, original.Categories[1]] },
            "add-category" => original with { Categories = [.. original.Categories, category with { Id = Guid.NewGuid() }] },
            _ => original with { Categories = [original.Categories[1]] },
        };
        CheckpointPolicy.For(LedgerPatch.Between(original, updated)).Value.ShouldBeOfType<ResetCheckpoints>();
        CheckpointPolicy.For(LedgerPatch.Between(updated, original)).Value.ShouldBeOfType<ResetCheckpoints>();
    }

    [Theory]
    [InlineData("amount", 0)]
    [InlineData("earlier", -1)]
    [InlineData("later", 0)]
    [InlineData("account", 0)]
    [InlineData("transfer", 0)]
    [InlineData("sequence", 0)]
    [InlineData("split-id", 0)]
    [InlineData("split-category", 0)]
    [InlineData("split-amount", 0)]
    [InlineData("no-splits", 0)]
    [InlineData("remove", 0)]
    [InlineData("template", 0)]
    public void PostedMovementChangesInvalidateBothOldAndNewDates(string change, int earliestMonth)
    {
        var original = Create();
        var entry = Entry(original, 0, -20) with { Sequence = 5 };
        original = original with { Transactions = [entry] };
        var changed = change switch
        {
            "amount" => entry with { Amount = -30 },
            "earlier" => entry with { Date = January.AddDays(-1) },
            "later" => entry with { Date = January.AddMonths(1) },
            "account" => entry with { AccountId = original.Accounts[1].Id },
            "transfer" => entry with { TransferAccountId = original.Accounts[1].Id },
            "sequence" => entry with { Sequence = 6 },
            "split-id" => entry with { Splits = [entry.Splits[0] with { Id = Guid.NewGuid() }] },
            "split-category" => entry with { Splits = [entry.Splits[0] with { CategoryId = null }] },
            "split-amount" => entry with { Splits = [entry.Splits[0] with { Amount = -30 }] },
            "no-splits" => entry with { Splits = [] },
            "template" => entry with { Repeat = RepeatFrequency.Monthly },
            _ => entry,
        };
        var updated = original with { Transactions = change is "remove" ? [] : [changed] };
        var expected = new InvalidateCheckpointsAfter(January.AddMonths(earliestMonth));
        CheckpointPolicy.For(LedgerPatch.Between(original, updated)).Value.ShouldBe(expected);
        CheckpointPolicy.For(LedgerPatch.Between(updated, original)).Value.ShouldBe(expected);
    }

    [Fact]
    public void AllocationAmountsUseEarliestFinancialMonthAndIgnoreEmptyRows()
    {
        var original = Create();
        var zero = new AllocationData(original.Categories[0].Id, January, 0, true);
        var empty = original with { Allocations = [zero] };
        CheckpointPolicy.For(LedgerPatch.Between(original, empty)).Value.ShouldBeOfType<PreserveCheckpoints>();
        CheckpointPolicy.For(LedgerPatch.Between(empty, original)).Value.ShouldBeOfType<PreserveCheckpoints>();
        var funded = empty with { Allocations = [zero, zero with { Month = January.AddMonths(1), Amount = 20 }] };
        CheckpointPolicy.For(LedgerPatch.Between(empty, funded)).Value.ShouldBe(new InvalidateCheckpointsAfter(January.AddMonths(1)));
        CheckpointPolicy.For(LedgerPatch.Between(funded, empty)).Value.ShouldBe(new InvalidateCheckpointsAfter(January.AddMonths(1)));
        var moved = funded with { Allocations = [zero with { Amount = 20 }] };
        CheckpointPolicy.For(LedgerPatch.Between(funded, moved)).Value.ShouldBe(new InvalidateCheckpointsAfter(January));
    }

    [Fact]
    public void MatchingTransferSidesAndReorderedSplitsPreserveStateButADifferentDestinationInvalidates()
    {
        var original = Create();
        var asset = original.Accounts[0] with { Id = Guid.NewGuid(), Kind = AccountKind.Asset };
        var otherAsset = asset with { Id = Guid.NewGuid() };
        var entry = Entry(original, 0, -20) with
        {
            TransferAccountId = asset.Id,
            Splits = [new(Guid.NewGuid(), original.Categories[0].Id, -10, "First"), new(Guid.NewGuid(), original.Categories[0].Id, -10, "Second")],
        };
        original = original with { Accounts = [.. original.Accounts, asset, otherAsset], Transactions = [entry] };
        var reordered = original with { Transactions = [entry with { Memo = "Changed", Splits = entry.Splits.Reverse().ToArray() }] };
        CheckpointPolicy.For(LedgerPatch.Between(original, reordered)).Value.ShouldBeOfType<PreserveCheckpoints>();
        var moved = original with { Transactions = [entry with { TransferAccountId = otherAsset.Id }] };
        CheckpointPolicy.For(LedgerPatch.Between(original, moved)).Value.ShouldBe(new InvalidateCheckpointsAfter(January));
    }

    [Fact]
    public void UnpostedTemplatesDoNotInvalidateOpenings()
    {
        var original = Create();
        var template = Entry(original, 0, -20) with { Repeat = RepeatFrequency.Monthly };
        var updated = original with { Transactions = [template] };
        CheckpointPolicy.For(LedgerPatch.Between(original, updated)).Value.ShouldBeOfType<PreserveCheckpoints>();
        CheckpointPolicy.For(LedgerPatch.Between(updated, original)).Value.ShouldBeOfType<PreserveCheckpoints>();
        CheckpointPolicy.For(LedgerPatch.Between(updated, updated with { Transactions = [template with { Amount = -30, Date = January.AddMonths(1) }] }))
            .Value.ShouldBeOfType<PreserveCheckpoints>();
    }
}
