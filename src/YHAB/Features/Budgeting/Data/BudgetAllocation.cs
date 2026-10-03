using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetAllocation
{
    public Guid PlanId { get; set; }
    public Guid CategoryId { get; set; }
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
    public bool Snoozed { get; set; }
}
