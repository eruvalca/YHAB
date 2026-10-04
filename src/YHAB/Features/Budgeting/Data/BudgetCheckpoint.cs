namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetCheckpoint
{
    public Guid PlanId { get; set; }
    public DateOnly Month { get; set; }
    public int FormatVersion { get; set; }
    public string State { get; set; } = "";
}
