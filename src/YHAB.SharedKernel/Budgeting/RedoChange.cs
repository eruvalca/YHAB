namespace YHAB.SharedKernel.Budgeting;

/// <summary>Redo Change within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record RedoChange(long Version) : PlanCommand(Version);
