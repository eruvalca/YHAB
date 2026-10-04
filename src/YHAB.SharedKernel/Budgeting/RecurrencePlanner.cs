namespace YHAB.SharedKernel.Budgeting;

/// <summary>Plans a bounded catch-up without allocating identities, consulting a clock, or changing a ledger.</summary>
public static class RecurrencePlanner
{
    public const int BatchSize = 128;

    public static RecurrenceBatch Due(TransactionData template, DateOnly through, int limit = BatchSize)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, BatchSize);
        if (template.Repeat == RepeatFrequency.None || !Enum.IsDefined(template.Repeat))
        {
            throw new ArgumentException("A repeating instruction is required.", nameof(template));
        }
        var dates = new List<DateOnly>();
        var date = template.Date;
        var occurrence = template.Occurrence;
        while (date <= through && dates.Count < limit)
        {
            dates.Add(date);
            occurrence++;
            date = RecurrenceCalendar.DateAt(template.AnchorDate ?? template.Date, template.Repeat, occurrence);
        }
        return new(dates, date, occurrence, date <= through);
    }
}
