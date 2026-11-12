using System.Text.Json.Serialization;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Client.Services;

/// <summary>Source-generated serialization metadata for the budgeting HTTP contracts.</summary>
/// <remarks>
/// Trim and Native AOT analysis reject reflection-based serialization, so every contract that
/// crosses the budgeting API is declared here. The options mirror
/// <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> to keep the wire format identical
/// to the server's reflection-based instance.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(PlanSummary[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(PlanSnapshot))]
[JsonSerializable(typeof(PlanView))]
[JsonSerializable(typeof(PlanMonthView))]
[JsonSerializable(typeof(BudgetMonth))]
[JsonSerializable(typeof(RegisterPage))]
[JsonSerializable(typeof(ReportView))]
[JsonSerializable(typeof(PlanShareSummary))]
[JsonSerializable(typeof(PlanShareCode))]
[JsonSerializable(typeof(RequestToken))]
[JsonSerializable(typeof(BudgetError))]
[JsonSerializable(typeof(CreatePlanRequest))]
[JsonSerializable(typeof(JoinPlanRequest))]
[JsonSerializable(typeof(RegisterQuery))]
[JsonSerializable(typeof(SaveAccount))]
[JsonSerializable(typeof(SaveGroup))]
[JsonSerializable(typeof(SaveCategory))]
[JsonSerializable(typeof(ReorderGroup))]
[JsonSerializable(typeof(ReorderCategory))]
[JsonSerializable(typeof(SaveTransaction))]
[JsonSerializable(typeof(DeleteTransactions))]
[JsonSerializable(typeof(AssignMoney))]
[JsonSerializable(typeof(MoveMoney))]
[JsonSerializable(typeof(AutoAssign))]
[JsonSerializable(typeof(UpdatePlan))]
[JsonSerializable(typeof(RenamePayee))]
[JsonSerializable(typeof(RemoveCategory))]
[JsonSerializable(typeof(UndoChange))]
[JsonSerializable(typeof(RedoChange))]
[JsonSerializable(typeof(PostRecurring))]
internal sealed partial class BudgetJsonContext : JsonSerializerContext;
