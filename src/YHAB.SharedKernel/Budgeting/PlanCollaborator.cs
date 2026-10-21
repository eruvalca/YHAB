namespace YHAB.SharedKernel.Budgeting;

/// <summary>An account that can open and edit a plan.</summary>
public sealed record PlanCollaborator(string UserId, string Name);
