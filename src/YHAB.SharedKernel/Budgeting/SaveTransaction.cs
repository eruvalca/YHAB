namespace YHAB.SharedKernel.Budgeting;

/// <summary>Save Transaction within one privately owned plan, conditional on its current revision.</summary>
public sealed record SaveTransaction(long Version, TransactionData Transaction) : PlanCommand(Version);
