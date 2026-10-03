using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal sealed class BudgetTransaction
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Payee { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid? TransferAccountId { get; set; }
    public ClearingState State { get; set; }
    public ClearingState TransferState { get; set; }
    public bool NeedsApproval { get; set; }
    public string Flag { get; set; } = string.Empty;
    public RepeatFrequency Repeat { get; set; }
    public DateOnly? AnchorDate { get; set; }
    public int Occurrence { get; set; }
    public Guid? SourceTemplateId { get; set; }
}
