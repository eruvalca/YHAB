namespace YHAB.SharedKernel.Budgeting;

/// <summary>Redo Change within one privately owned plan, conditional on its current revision.</summary>
public sealed record RedoChange(long Version) : PlanCommand(Version);
