namespace YHAB.SharedKernel.Budgeting;

/// <summary>Plan Snapshot exchanged between the budgeting service and its clients.</summary>
public sealed record PlanSnapshot(Guid Id, string Name, string Notes, DateOnly CreatedOn, long Version, IReadOnlyList<AccountData> Accounts, IReadOnlyList<GroupData> Groups, IReadOnlyList<CategoryData> Categories, IReadOnlyList<AllocationData> Allocations, IReadOnlyList<TransactionData> Transactions, bool CanUndo, bool CanRedo, IReadOnlyList<ChangeData> Changes)
{
    public DateOnly Today { get; init; }
}
