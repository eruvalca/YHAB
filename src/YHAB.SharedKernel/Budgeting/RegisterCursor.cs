namespace YHAB.SharedKernel.Budgeting;

public sealed record RegisterCursor(DateOnly Date, long Sequence, Guid Id, string Payee, decimal Amount);
