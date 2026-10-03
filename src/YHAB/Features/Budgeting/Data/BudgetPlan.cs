using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetPlan
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateOnly CreatedOn { get; set; }
    public long Version { get; set; }
    public int HistoryCursor { get; set; }
}
