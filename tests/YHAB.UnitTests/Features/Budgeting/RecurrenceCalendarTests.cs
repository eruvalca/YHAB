using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class RecurrenceCalendarTests
{
    [Theory]
    [InlineData(RepeatFrequency.Daily, 1, 2024, 3, 1)]
    [InlineData(RepeatFrequency.Weekly, 1, 2024, 3, 7)]
    [InlineData(RepeatFrequency.EveryTwoWeeks, 1, 2024, 3, 14)]
    [InlineData(RepeatFrequency.Monthly, 1, 2024, 3, 29)]
    [InlineData(RepeatFrequency.EveryTwoMonths, 1, 2024, 4, 29)]
    [InlineData(RepeatFrequency.Quarterly, 1, 2024, 5, 29)]
    [InlineData(RepeatFrequency.EverySixMonths, 1, 2024, 8, 29)]
    [InlineData(RepeatFrequency.Yearly, 1, 2025, 2, 28)]
    [InlineData(RepeatFrequency.Yearly, 4, 2028, 2, 29)]
    [InlineData(RepeatFrequency.Monthly, 0, 2024, 2, 29)]
    public void FrequenciesRetainTheAnchorAcrossCalendarBoundaries(RepeatFrequency frequency, int occurrence, int year, int month, int day)
        => RecurrenceCalendar.DateAt(new(2024, 2, 29), frequency, occurrence).ShouldBe(new(year, month, day));

    [Fact]
    public void TwiceMonthlyClampsFebruaryAndReturnsToTheOriginalDays()
    {
        var anchor = new DateOnly(2026, 1, 30);
        Enumerable.Range(0, 5).Select(index => RecurrenceCalendar.DateAt(anchor, RepeatFrequency.TwiceMonthly, index))
            .ShouldBe([new(2026, 1, 30), new(2026, 2, 15), new(2026, 2, 28), new(2026, 3, 15), new(2026, 3, 30)]);
    }
}
