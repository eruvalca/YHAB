using System.Text.Json.Serialization;

namespace YHAB.SharedKernel.Budgeting;

[JsonConverter(typeof(JsonStringEnumConverter<ClearingState>))]
public enum ClearingState
{
    Uncleared,
    Cleared,
    Reconciled
}
