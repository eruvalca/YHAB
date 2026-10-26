using System.Globalization;
using System.Text;

namespace YHAB.UI.Features.Budgeting.Components;

/// <summary>
/// Composes the value snapshot an editor compares to decide whether a draft is dirty. Each field is
/// length-prefixed, so a separator typed inside a field cannot imitate a field boundary: the payee
/// "Shop|branch" with memo "note" and the payee "Shop" with memo "branch|note" stay distinct.
/// </summary>
internal static class DraftSignature
{
    public static string Of(params object?[] fields)
    {
        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            var text = Convert.ToString(field, CultureInfo.InvariantCulture) ?? string.Empty;
            builder.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
        }
        return builder.ToString();
    }
}
