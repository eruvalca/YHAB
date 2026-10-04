namespace YHAB.SharedKernel.Budgeting;

public sealed record RegisterPage(long Version, IReadOnlyList<RegisterRow> Rows, int Total, RegisterCursor? Next);
