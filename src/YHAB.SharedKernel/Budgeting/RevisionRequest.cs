namespace YHAB.SharedKernel.Budgeting;

/// <summary>Revision Request exchanged between the budgeting service and its clients.</summary>
public sealed record RevisionRequest(long Version);
