namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetPlanShareCodeRedemption
{
    public string CodeHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public DateTimeOffset RedeemedOn { get; set; }
}
