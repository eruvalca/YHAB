using System.Text.Json.Serialization;

namespace YHAB.SharedKernel.Budgeting;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
[JsonDerivedType(typeof(SaveAccount), "SaveAccount")]
[JsonDerivedType(typeof(SaveGroup), "SaveGroup")]
[JsonDerivedType(typeof(SaveCategory), "SaveCategory")]
[JsonDerivedType(typeof(SaveTransaction), "SaveTransaction")]
[JsonDerivedType(typeof(DeleteTransactions), "DeleteTransactions")]
[JsonDerivedType(typeof(UpdateTransactionStates), "UpdateTransactionStates")]
[JsonDerivedType(typeof(AssignMoney), "AssignMoney")]
[JsonDerivedType(typeof(MoveMoney), "MoveMoney")]
[JsonDerivedType(typeof(AutoAssign), "AutoAssign")]
[JsonDerivedType(typeof(ReconcileAccount), "ReconcileAccount")]
[JsonDerivedType(typeof(UpdatePlan), "UpdatePlan")]
[JsonDerivedType(typeof(RenamePayee), "RenamePayee")]
[JsonDerivedType(typeof(RemoveCategory), "RemoveCategory")]
[JsonDerivedType(typeof(UndoChange), "UndoChange")]
[JsonDerivedType(typeof(RedoChange), "RedoChange")]
[JsonDerivedType(typeof(PostRecurring), "PostRecurring")]
public abstract record PlanCommand(long Version)
{
    // Created once per user operation; retain this value when retrying an interrupted request.
    public Guid OperationId { get; init; } = Guid.CreateVersion7();
}
