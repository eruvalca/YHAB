namespace YHAB.SharedKernel.Budgeting;

public sealed record RecurrenceBatch(IReadOnlyList<DateOnly> Dates, DateOnly NextDate, int NextOccurrence, bool HasMore);
