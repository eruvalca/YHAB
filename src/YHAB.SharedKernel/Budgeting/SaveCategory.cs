namespace YHAB.SharedKernel.Budgeting;

/// <summary>Save Category within one privately owned plan, conditional on its current revision.</summary>
public sealed record SaveCategory(long Version, CategoryData Category) : PlanCommand(Version);
