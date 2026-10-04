using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HistoryPolicyTests
{
    [Theory]
    [InlineData(0, 50, true)]
    [InlineData(1, 50, true)]
    [InlineData(2, 50, false)]
    [InlineData(50, 50, false)]
    [InlineData(51, 50, true)]
    public void AppendRetainsFiftyChangesAndDiscardsTheRedoBranch(long position, long cursor, bool discarded)
        => HistoryPolicy.DiscardOnAppend(position, cursor).ShouldBe(discarded);

    [Theory]
    [InlineData(false, 7, 6)]
    [InlineData(true, 8, 8)]
    public void HistoryDirectionChoosesPositionAndCursor(bool redo, int position, int cursor)
    {
        HistoryPolicy.RequestedPosition(7, redo).ShouldBe(position);
        HistoryPolicy.RestoredCursor(7, redo).ShouldBe(cursor);
    }

    [Fact]
    public void AssignmentUndoPreservesLaterSystemTransactionsAndRedoRestoresOnlyTheAssignment()
    {
        var before = Create();
        var after = Assigned(before, 123.45m);
        var patch = LedgerPatch.Between(before, after);
        patch.Before.Transactions.ShouldBeEmpty();
        patch.After.Accounts.ShouldBeEmpty();
        patch.IsEmpty.ShouldBeFalse();
        var systemEntry = Entry(after, 0, 200, category: -1);
        var current = after with { Version = 8, Transactions = [systemEntry] };
        patch.TryApply(current, true, out var undone).ShouldBeTrue();
        undone.Allocations.ShouldBeEmpty();
        undone.Transactions.ShouldBe([systemEntry]);
        undone.Version.ShouldBe(8);
        patch.TryApply(undone, false, out var redone).ShouldBeTrue();
        redone.Allocations.ShouldBe(after.Allocations);
        redone.Transactions.ShouldBe([systemEntry]);
    }

    [Fact]
    public void PatchesUseStructuralSplitEqualityAfterJsonRoundTrip()
    {
        var before = Create();
        before = before with { Transactions = [Entry(before, 0, -10)] };
        var copy = JsonSerializer.Deserialize<PlanSnapshot>(JsonSerializer.Serialize(before)).ShouldNotBeNull();
        LedgerPatch.Between(before, copy).IsEmpty.ShouldBeTrue();
        var after = copy with { Transactions = [copy.Transactions[0] with { Memo = "Edited" }] };
        var patch = LedgerPatch.Between(before, after);
        patch.TryApply(after, true, out var undone).ShouldBeTrue();
        undone.Transactions[0].Memo.ShouldBe("");
        undone.Transactions[0].Splits.ShouldBe(before.Transactions[0].Splits);
    }

    [Fact]
    public void UndoRejectsConflictingRecordsAndNewDependentsWithoutChangingCurrentState()
    {
        var before = Create();
        var account = before.Accounts[0] with { Id = Guid.NewGuid(), Name = "New account" };
        var after = before with { Accounts = [.. before.Accounts, account] };
        var patch = LedgerPatch.Between(before, after);
        var dependent = after with { Transactions = [Entry(after, 0, 10, category: -1) with { AccountId = account.Id }] };
        patch.TryApply(dependent, true, out var unchanged).ShouldBeFalse();
        unchanged.ShouldBeSameAs(dependent);
        var conflicting = after with { Accounts = [.. before.Accounts, account with { Name = "Another edit" }] };
        patch.TryApply(conflicting, true, out unchanged).ShouldBeFalse();
        unchanged.ShouldBeSameAs(conflicting);
    }

    [Fact]
    public void MetadataChangesPreserveUnrelatedEditsAndRejectWrongPlanOrDuplicateInsertion()
    {
        var before = Create();
        var after = before with { Name = "Renamed" };
        var patch = LedgerPatch.Between(before, after);
        patch.TryApply(after with { Notes = "Later note" }, true, out var undone).ShouldBeTrue();
        undone.Name.ShouldBe(before.Name);
        undone.Notes.ShouldBe("Later note");
        patch.TryApply(after with { Id = Guid.NewGuid() }, true, out _).ShouldBeFalse();
        patch.TryApply(after with { Name = "Conflicting rename" }, true, out _).ShouldBeFalse();
        var assigned = Assigned(before, 10);
        LedgerPatch.Between(before, assigned).TryApply(assigned, false, out _).ShouldBeFalse();
    }

    [Fact]
    public void SplitStorageOrderDoesNotCreateFalseConflicts()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -10);
        entry = entry with { Splits = [entry.Splits[0] with { Amount = -4 }, entry.Splits[0] with { Id = Guid.NewGuid(), Amount = -6 }] };
        plan = plan with { Transactions = [entry] };
        var reordered = plan with { Transactions = [entry with { Splits = entry.Splits.Reverse().ToArray() }] };
        LedgerPatch.Between(plan, reordered).IsEmpty.ShouldBeTrue();
        var changed = plan with { Transactions = [entry with { Memo = "Changed" }] };
        LedgerPatch.Between(plan, changed).TryApply(reordered, false, out var applied).ShouldBeTrue();
        applied.Transactions.Single().Memo.ShouldBe("Changed");
    }

    [Fact]
    public void NotesAndEachRecordTypeDetectConflictingChanges()
    {
        var plan = Assigned(Create(), 10);
        plan = plan with { Transactions = [Entry(plan, 0, -10)] };
        PlanSnapshot[] changes =
        [
            plan with { Notes = "Changed" },
            plan with { Groups = [plan.Groups[0] with { Name = "Changed" }] },
            plan with { Categories = [plan.Categories[0] with { Name = "Changed" }, plan.Categories[1]] },
            plan with { Allocations = [plan.Allocations[0] with { Amount = 12 }] },
            plan with { Transactions = [plan.Transactions[0] with { Amount = -12 }] },
        ];
        foreach (var changed in changes)
        {
            var patch = LedgerPatch.Between(plan, changed);
            patch.IsEmpty.ShouldBeFalse();
            patch.TryApply(plan, true, out var unchanged).ShouldBeFalse();
            unchanged.ShouldBeSameAs(plan);
            patch.TryApply(changed, true, out var restored).ShouldBeTrue();
            LedgerPatch.Between(plan, restored).IsEmpty.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("group")]
    [InlineData("payment-account")]
    [InlineData("allocation")]
    [InlineData("split")]
    [InlineData("transfer")]
    public void UndoCannotOrphanLaterReferences(string kind)
    {
        var current = Assigned(Create(), 10);
        current = current with { Transactions = [Entry(current, 0, -5)] };
        var desired = kind switch
        {
            "group" => current with { Groups = [] },
            "payment-account" => current with { Accounts = [current.Accounts[0]] },
            "allocation" => current with { Categories = [current.Categories[1]], Transactions = [] },
            "split" => current with { Categories = [current.Categories[1]], Allocations = [] },
            _ => current with { Accounts = [current.Accounts[1]], Allocations = [], Transactions = [] },
        };
        if (string.Equals(kind, "transfer", StringComparison.Ordinal))
        {
            current = current with { Transactions = [Entry(current, 1, -10) with { TransferAccountId = current.Accounts[0].Id, Splits = [] }] };
            desired = desired with { Transactions = current.Transactions };
        }
        var patch = LedgerPatch.Between(desired, current);
        patch.TryApply(current, true, out var result).ShouldBeFalse();
        result.ShouldBeSameAs(current);
    }

    [Fact]
    public void UndoCannotRestoreOverAMissingExpectedTransaction()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -10);
        var before = plan with { Transactions = [entry] };
        var after = before with { Transactions = [entry with { Memo = "Edited" }] };
        LedgerPatch.Between(before, after).TryApply(plan, true, out var result).ShouldBeFalse();
        result.ShouldBeSameAs(plan);
    }
}
