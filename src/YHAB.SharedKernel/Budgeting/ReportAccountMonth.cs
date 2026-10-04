namespace YHAB.SharedKernel.Budgeting;

/// <summary>Net posted movement on one side of an account, grouped by report month.</summary>
public sealed record ReportAccountMonth(Guid AccountId, DateOnly Month, decimal Amount);
