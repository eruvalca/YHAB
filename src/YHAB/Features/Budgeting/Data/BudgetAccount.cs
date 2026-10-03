using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetAccount
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountKind Kind { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateOnly OpenedOn { get; set; }
    public bool Closed { get; set; }
    public string Notes { get; set; } = string.Empty;
    public decimal InterestRate { get; set; }
    public decimal MinimumPayment { get; set; }
}
