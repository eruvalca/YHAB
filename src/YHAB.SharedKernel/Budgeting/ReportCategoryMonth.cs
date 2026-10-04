namespace YHAB.SharedKernel.Budgeting;

/// <summary>Signed movement into the budget by month and category; a null category represents income.</summary>
public sealed record ReportCategoryMonth(Guid? CategoryId, DateOnly Month, decimal Amount);
