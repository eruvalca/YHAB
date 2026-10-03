namespace YHAB.SharedKernel.Budgeting;

/// <summary>Target Data exchanged between the budgeting service and its clients.</summary>
public sealed record TargetData(TargetKind Kind, TargetCadence Cadence, decimal Amount, DateOnly StartMonth, DateOnly? DueDate, int RepeatEveryMonths = 0, DayOfWeek Weekday = DayOfWeek.Friday);
