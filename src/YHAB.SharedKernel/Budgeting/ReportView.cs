namespace YHAB.SharedKernel.Budgeting;

public sealed record ReportView(long Version, IReadOnlyList<MonthReport> Months, IReadOnlyList<CategorySpending> Spending);
