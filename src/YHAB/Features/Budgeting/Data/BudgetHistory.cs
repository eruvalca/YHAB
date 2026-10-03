using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetHistory
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public int Position { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}
