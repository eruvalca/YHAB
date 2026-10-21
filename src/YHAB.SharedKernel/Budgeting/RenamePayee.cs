namespace YHAB.SharedKernel.Budgeting;

/// <summary>Rename Payee within one plan the caller can edit, conditional on its current revision.</summary>
public sealed record RenamePayee(long Version, string OldName, string NewName) : PlanCommand(Version);
