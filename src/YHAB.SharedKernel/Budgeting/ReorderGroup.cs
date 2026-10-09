namespace YHAB.SharedKernel.Budgeting;

/// <summary>Move a group before another group, or to the end when the anchor is null.</summary>
public sealed record ReorderGroup(long Version, Guid GroupId, Guid? BeforeGroupId) : PlanCommand(Version);
