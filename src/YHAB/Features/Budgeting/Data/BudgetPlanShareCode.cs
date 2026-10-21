namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetPlanShareCode
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresOn { get; set; }
}
