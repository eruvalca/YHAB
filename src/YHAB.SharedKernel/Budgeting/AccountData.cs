namespace YHAB.SharedKernel.Budgeting;

/// <summary>Account Data exchanged between the budgeting service and its clients.</summary>
public sealed record AccountData(Guid Id, string Name, AccountKind Kind, decimal OpeningBalance, DateOnly OpenedOn, bool Closed, string Notes, decimal InterestRate = 0, decimal MinimumPayment = 0);
