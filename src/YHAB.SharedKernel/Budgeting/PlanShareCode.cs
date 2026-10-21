namespace YHAB.SharedKernel.Budgeting;

/// <summary>A one-time code the owner gives to a person who should join the plan.</summary>
public sealed record PlanShareCode(Guid PlanId, string Code, DateTimeOffset ExpiresOn);
