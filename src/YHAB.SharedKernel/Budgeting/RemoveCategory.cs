namespace YHAB.SharedKernel.Budgeting;

/// <summary>Remove Category within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record RemoveCategory(long Version, Guid CategoryId, Guid? ReplacementCategoryId) : PlanCommand(Version);
