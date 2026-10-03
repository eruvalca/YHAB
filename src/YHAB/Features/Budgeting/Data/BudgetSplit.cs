using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetSplit
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal Amount { get; set; }
    public string Memo { get; set; } = string.Empty;
}
