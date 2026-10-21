namespace YHAB.SharedKernel.Budgeting;

/// <summary>Plan Summary exchanged between the budgeting service and its clients.</summary>
public sealed record PlanSummary(Guid Id, string Name, string Notes, long Version)
{
    public bool IsOwner { get; init; } = true;
    public string? SharedBy { get; init; }
}
