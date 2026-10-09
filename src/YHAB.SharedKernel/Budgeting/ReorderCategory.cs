namespace YHAB.SharedKernel.Budgeting;

/// <summary>Move a category into a group before an anchor, or to the end when the anchor is null.</summary>
public sealed record ReorderCategory(long Version, Guid CategoryId, Guid GroupId, Guid? BeforeCategoryId) : PlanCommand(Version);
