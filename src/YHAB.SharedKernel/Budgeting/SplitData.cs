namespace YHAB.SharedKernel.Budgeting;

/// <summary>Split Data exchanged between the budgeting service and its clients.</summary>
public sealed record SplitData(Guid Id, Guid? CategoryId, decimal Amount, string Memo);
