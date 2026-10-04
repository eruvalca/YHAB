namespace YHAB.UI.Features.Budgeting;

internal static class MoneyPresentation
{
    internal static string MoneyTone(decimal amount) => amount switch
    {
        > 0 => "positive",
        < 0 => "negative",
        _ => "neutral",
    };
}
