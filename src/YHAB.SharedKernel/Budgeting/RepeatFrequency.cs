using System.Text.Json.Serialization;

namespace YHAB.SharedKernel.Budgeting;

[JsonConverter(typeof(JsonStringEnumConverter<RepeatFrequency>))]
public enum RepeatFrequency
{
    None,
    Daily,
    Weekly,
    EveryTwoWeeks,
    TwiceMonthly,
    Monthly,
    EveryTwoMonths,
    Quarterly,
    EverySixMonths,
    Yearly
}
