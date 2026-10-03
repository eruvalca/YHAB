namespace YHAB.SharedKernel.Budgeting;

/// <summary>Post Recurring within one privately owned plan, conditional on its current revision.</summary>
public sealed record PostRecurring(long Version, DateOnly ThroughDate) : PlanCommand(Version);
