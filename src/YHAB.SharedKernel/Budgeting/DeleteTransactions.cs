namespace YHAB.SharedKernel.Budgeting;

/// <summary>Delete Transactions within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record DeleteTransactions(long Version, IReadOnlyList<Guid> TransactionIds) : PlanCommand(Version);
