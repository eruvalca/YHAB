namespace YHAB.SharedKernel.Budgeting;

/// <summary>Reconcile Account within one privately owned plan, conditional on its current revision.</summary>
public sealed record ReconcileAccount(long Version, Guid AccountId, DateOnly Date, decimal ClearedBalance, bool CreateAdjustment) : PlanCommand(Version);
