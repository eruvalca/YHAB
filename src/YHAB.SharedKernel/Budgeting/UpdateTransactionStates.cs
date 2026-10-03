namespace YHAB.SharedKernel.Budgeting;

/// <summary>Update Transaction States within one privately owned plan, conditional on its current revision.</summary>
public sealed record UpdateTransactionStates(long Version, IReadOnlyList<Guid> TransactionIds, ClearingState? State, bool Approve, Guid? AccountId = null) : PlanCommand(Version);
