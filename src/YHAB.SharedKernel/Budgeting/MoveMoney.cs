namespace YHAB.SharedKernel.Budgeting;

/// <summary>Move Money within one privately owned plan, conditional on its current revision.</summary>
public sealed record MoveMoney(long Version, Guid? FromCategoryId, Guid? ToCategoryId, DateOnly Month, decimal Amount) : PlanCommand(Version);
