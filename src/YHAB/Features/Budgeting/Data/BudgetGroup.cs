using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetGroup
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
}
