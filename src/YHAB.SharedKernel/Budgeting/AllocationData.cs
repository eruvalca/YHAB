namespace YHAB.SharedKernel.Budgeting;

/// <summary>Allocation Data exchanged between the budgeting service and its clients.</summary>
public sealed record AllocationData(Guid CategoryId, DateOnly Month, decimal Amount, bool Snoozed = false);
