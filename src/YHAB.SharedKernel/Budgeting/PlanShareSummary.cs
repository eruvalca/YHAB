namespace YHAB.SharedKernel.Budgeting;

/// <summary>Sharing state for one plan, including whether the viewer owns it.</summary>
public sealed record PlanShareSummary(string OwnerName, bool IsOwner, IReadOnlyList<PlanCollaborator> Collaborators);
