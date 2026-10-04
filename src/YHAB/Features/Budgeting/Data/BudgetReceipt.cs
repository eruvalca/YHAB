namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetReceipt
{
    public Guid PlanId { get; set; }
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public long Version { get; set; }
}
