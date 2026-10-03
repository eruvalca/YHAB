namespace YHAB.SharedKernel.Budgeting;

/// <summary>Change Data exchanged between the budgeting service and its clients.</summary>
public sealed record ChangeData(string Description, DateTimeOffset Timestamp);
