using System.Text.Json.Serialization;

namespace YHAB.SharedKernel.Budgeting;

[JsonConverter(typeof(JsonStringEnumConverter<TargetCadence>))]
public enum TargetCadence
{
    Weekly,
    Monthly,
    Yearly,
    Custom
}
