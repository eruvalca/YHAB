namespace YHAB.SharedKernel.Budgeting;

/// <summary>Transaction Data exchanged between the budgeting service and its clients.</summary>
public sealed record TransactionData(Guid Id, Guid AccountId, DateOnly Date, string Payee, string Memo, decimal Amount, Guid? TransferAccountId, ClearingState State, ClearingState TransferState, bool NeedsApproval, string Flag, IReadOnlyList<SplitData> Splits, RepeatFrequency Repeat = RepeatFrequency.None, DateOnly? AnchorDate = null, int Occurrence = 0, Guid? SourceTemplateId = null);
