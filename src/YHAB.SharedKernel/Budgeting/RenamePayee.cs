namespace YHAB.SharedKernel.Budgeting;

/// <summary>Rename Payee within one privately owned plan, conditional on its current revision.</summary>
public sealed record RenamePayee(long Version, string OldName, string NewName) : PlanCommand(Version);
