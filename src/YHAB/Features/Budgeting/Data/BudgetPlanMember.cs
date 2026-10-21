namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetPlanMember
{
    public Guid PlanId { get; set; }
    public string UserId { get; set; } = string.Empty;
}
