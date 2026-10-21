namespace YHAB.SharedKernel.Budgeting;

/// <summary>Undo Change within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record UndoChange(long Version) : PlanCommand(Version);
