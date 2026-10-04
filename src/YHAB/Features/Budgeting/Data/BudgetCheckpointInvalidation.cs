namespace YHAB.Features.Budgeting.Data;

/// <summary>Earliest opening invalidated by a committed financial revision.</summary>
internal sealed class BudgetCheckpointInvalidation
{
    public Guid PlanId { get; set; }
    public long Version { get; set; }
    public DateOnly FirstInvalidMonth { get; set; }
}
