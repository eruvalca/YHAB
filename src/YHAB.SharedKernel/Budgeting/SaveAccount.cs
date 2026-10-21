namespace YHAB.SharedKernel.Budgeting;

/// <summary>Save Account within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record SaveAccount(long Version, AccountData Account) : PlanCommand(Version);
