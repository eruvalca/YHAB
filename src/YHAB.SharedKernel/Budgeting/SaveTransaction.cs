namespace YHAB.SharedKernel.Budgeting;

/// <summary>Save Transaction within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record SaveTransaction(long Version, TransactionData Transaction) : PlanCommand(Version);
