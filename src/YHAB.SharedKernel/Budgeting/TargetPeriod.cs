namespace YHAB.SharedKernel.Budgeting;

/// <summary>The inclusive months in the active cycle of a dated target.</summary>
public sealed record TargetPeriod(DateOnly Start, DateOnly Due);
