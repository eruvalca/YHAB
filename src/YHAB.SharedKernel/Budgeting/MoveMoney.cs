namespace YHAB.SharedKernel.Budgeting;

/// <summary>Move Money within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record MoveMoney(long Version, Guid? FromCategoryId, Guid? ToCategoryId, DateOnly Month, decimal Amount) : PlanCommand(Version);
