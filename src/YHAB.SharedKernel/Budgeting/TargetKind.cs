using System.Text.Json.Serialization;

namespace YHAB.SharedKernel.Budgeting;

[JsonConverter(typeof(JsonStringEnumConverter<TargetKind>))]
public enum TargetKind
{
    Refill,
    SetAside,
    Balance
}
