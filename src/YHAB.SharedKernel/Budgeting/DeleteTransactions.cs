namespace YHAB.SharedKernel.Budgeting;

/// <summary>Delete Transactions within one privately owned plan, conditional on its current revision.</summary>
public sealed record DeleteTransactions(long Version, IReadOnlyList<Guid> TransactionIds) : PlanCommand(Version);
