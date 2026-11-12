using System.Text.Json.Serialization;

namespace YHAB.SharedKernel.Budgeting;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
[JsonDerivedType(typeof(SaveAccount), "SaveAccount")]
[JsonDerivedType(typeof(SaveGroup), "SaveGroup")]
[JsonDerivedType(typeof(SaveCategory), "SaveCategory")]
[JsonDerivedType(typeof(ReorderGroup), "ReorderGroup")]
[JsonDerivedType(typeof(ReorderCategory), "ReorderCategory")]
[JsonDerivedType(typeof(SaveTransaction), "SaveTransaction")]
[JsonDerivedType(typeof(DeleteTransactions), "DeleteTransactions")]
[JsonDerivedType(typeof(AssignMoney), "AssignMoney")]
[JsonDerivedType(typeof(MoveMoney), "MoveMoney")]
[JsonDerivedType(typeof(AutoAssign), "AutoAssign")]
[JsonDerivedType(typeof(UpdatePlan), "UpdatePlan")]
[JsonDerivedType(typeof(RenamePayee), "RenamePayee")]
[JsonDerivedType(typeof(RemoveCategory), "RemoveCategory")]
[JsonDerivedType(typeof(UndoChange), "UndoChange")]
[JsonDerivedType(typeof(RedoChange), "RedoChange")]
[JsonDerivedType(typeof(PostRecurring), "PostRecurring")]
// Closed: every direct subtype is declared in this assembly, so a switch that names each one is
// exhaustiveness-checked (CS8509) wherever it is written. Add a command here and every dispatch
// must answer for it.
public closed record PlanCommand(long Version)
{
    // Created once per user operation; retain this value when retrying an interrupted request.
    public Guid OperationId { get; init; } = Guid.CreateVersion7();
}
