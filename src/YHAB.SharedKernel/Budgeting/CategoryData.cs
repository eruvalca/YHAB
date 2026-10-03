namespace YHAB.SharedKernel.Budgeting;

/// <summary>Category Data exchanged between the budgeting service and its clients.</summary>
public sealed record CategoryData(Guid Id, Guid GroupId, string Name, string Notes, int SortOrder, bool Hidden, Guid? CreditAccountId, TargetData? Target);
