using System.Text.Json.Serialization;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting;

/// <summary>
/// Source-generated metadata for measuring the display snapshot before it is
/// handed to the interactive renderer. Reflection-based serialization is not
/// used because this code is also published with the trimmed WebAssembly bundle.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PlanView))]
[JsonSerializable(typeof(BudgetMonth))]
internal sealed partial class WorkspaceDisplayJsonContext : JsonSerializerContext;
