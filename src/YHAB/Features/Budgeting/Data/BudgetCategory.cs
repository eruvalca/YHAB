using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetCategory
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public Guid? CreditAccountId { get; set; }
    public TargetKind? TargetKind { get; set; }
    public TargetCadence TargetCadence { get; set; }
    public decimal TargetAmount { get; set; }
    public DateOnly TargetStartMonth { get; set; }
    public DateOnly? TargetDueDate { get; set; }
    public int TargetRepeatMonths { get; set; }
    public DayOfWeek TargetWeekday { get; set; }
}
