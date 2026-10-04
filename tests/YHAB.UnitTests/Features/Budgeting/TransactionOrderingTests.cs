using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class TransactionOrderingTests
{
    [Fact]
    public void NewEntriesIgnoreClientSequencesAndEditsKeepTheirOrder()
    {
        var existing = Entry(Create(), 0, 10) with { Sequence = 4 };
        var added = existing with { Id = Guid.NewGuid(), Sequence = 999 };
        var ordered = TransactionOrdering.Assign([existing], [existing with { Sequence = 0, Date = January }, added], 2, false);
        ordered.Transactions.Select(item => item.Sequence).ShouldBe([4, 5]);
        ordered.LastSequence.ShouldBe(5);
        ordered.Transactions[0].Date.ShouldBe(January);
    }

    [Fact]
    public void RestoringKeepsOriginalOrderAndNeverRewindsTheCounter()
    {
        var restored = Entry(Create(), 0, 10) with { Sequence = 4 };
        var result = TransactionOrdering.Assign([], [restored], 9, true);
        result.Transactions[0].Sequence.ShouldBe(4);
        result.LastSequence.ShouldBe(9);
        TransactionOrdering.Assign([], [restored], 0, true).LastSequence.ShouldBe(4);
        TransactionOrdering.Assign([], [], 9, false).LastSequence.ShouldBe(9);
    }

    [Fact]
    public void RestoringLegacyZeroSequenceDoesNotAllocateOrChangeOrder()
    {
        var restored = Entry(Create(), 0, 10) with { Sequence = 0 };
        var result = TransactionOrdering.Assign([], [restored], long.MaxValue, true);
        result.Transactions.Single().ShouldBe(restored);
        result.LastSequence.ShouldBe(long.MaxValue);
    }

    [Fact]
    public void ExhaustedSequencesAndMissingInputsFailExplicitly()
    {
        Should.Throw<OverflowException>(() => TransactionOrdering.Assign([], [Entry(Create(), 0, 1)], long.MaxValue, false));
        Should.Throw<ArgumentNullException>(() => TransactionOrdering.Assign(null!, [], 0, false));
        Should.Throw<ArgumentNullException>(() => TransactionOrdering.Assign([], null!, 0, false));
    }
}
