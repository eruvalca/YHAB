namespace YHAB.SharedKernel.Budgeting;

/// <summary>Group Data exchanged between the budgeting service and its clients.</summary>
public sealed record GroupData(Guid Id, string Name, int SortOrder, bool Hidden = false);
