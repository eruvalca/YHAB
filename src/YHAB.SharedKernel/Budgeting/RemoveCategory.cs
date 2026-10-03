namespace YHAB.SharedKernel.Budgeting;

/// <summary>Remove Category within one privately owned plan, conditional on its current revision.</summary>
public sealed record RemoveCategory(long Version, Guid CategoryId, Guid? ReplacementCategoryId) : PlanCommand(Version);
