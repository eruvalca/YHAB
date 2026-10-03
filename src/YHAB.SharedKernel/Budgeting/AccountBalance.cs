namespace YHAB.SharedKernel.Budgeting;

/// <summary>Account Balance exchanged between the budgeting service and its clients.</summary>
public sealed record AccountBalance(Guid AccountId, decimal Cleared, decimal Uncleared, decimal Reconciled, decimal Working);
