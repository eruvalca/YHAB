using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class RecurrencePlannerTests
{
    [Fact]
    public void CatchUpResumesAtTheNextOccurrenceWithoutSkippingOrDuplicatingDates()
    {
        var template = Entry(Create(), 0, -1, date: January) with { Repeat = RepeatFrequency.Daily, AnchorDate = January };
        var first = RecurrencePlanner.Due(template, January.AddDays(129));
        first.Dates.Count.ShouldBe(128);
        first.Dates[0].ShouldBe(January);
        first.Dates[^1].ShouldBe(January.AddDays(127));
        first.HasMore.ShouldBeTrue();
        var second = RecurrencePlanner.Due(template with { Date = first.NextDate, Occurrence = first.NextOccurrence }, January.AddDays(129));
        second.Dates.ShouldBe([January.AddDays(128), January.AddDays(129)]);
        second.NextOccurrence.ShouldBe(130);
        second.NextDate.ShouldBe(January.AddDays(130));
        second.HasMore.ShouldBeFalse();
        template.Date.ShouldBe(January);
        template.Occurrence.ShouldBe(0);
    }

    [Fact]
    public void FutureTemplatesRemainUnchangedAndMonthEndsRetainTheAnchor()
    {
        var template = Entry(Create(), 0, -1, date: new(2024, 1, 31)) with { Repeat = RepeatFrequency.Monthly };
        var future = RecurrencePlanner.Due(template, new(2024, 1, 30));
        future.Dates.ShouldBeEmpty();
        future.NextDate.ShouldBe(template.Date);
        future.NextOccurrence.ShouldBe(0);
        future.HasMore.ShouldBeFalse();
        var due = RecurrencePlanner.Due(template, new(2024, 3, 31));
        due.Dates.ShouldBe([new(2024, 1, 31), new(2024, 2, 29), new(2024, 3, 31)]);
        due.NextDate.ShouldBe(new(2024, 4, 30));
    }

    [Fact]
    public void InvalidInstructionsAndBatchLimitsAreRejected()
    {
        var template = Entry(Create(), 0, -1);
        Should.Throw<ArgumentNullException>(() => RecurrencePlanner.Due(null!, January));
        Should.Throw<ArgumentException>(() => RecurrencePlanner.Due(template, January));
        Should.Throw<ArgumentException>(() => RecurrencePlanner.Due(template with { Repeat = (RepeatFrequency)999 }, January));
        Should.Throw<ArgumentOutOfRangeException>(() => RecurrencePlanner.Due(template, January, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => RecurrencePlanner.Due(template, January, 129));
    }
}
