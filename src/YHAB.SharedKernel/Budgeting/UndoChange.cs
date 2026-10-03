namespace YHAB.SharedKernel.Budgeting;

/// <summary>Undo Change within one privately owned plan, conditional on its current revision.</summary>
public sealed record UndoChange(long Version) : PlanCommand(Version);
