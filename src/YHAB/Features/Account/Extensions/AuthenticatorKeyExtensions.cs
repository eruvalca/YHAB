using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace YHAB.Features.Account.Extensions;

internal static class AuthenticatorKeyExtensions
{
    extension(string unformattedKey)
    {
        [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
            Justification = "Lowercase grouping is only for the displayed authenticator key; the stored key and QR-code URI use the original value.")]
        internal string FormatAuthenticatorKey()
        {
            var result = new StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
                currentPosition += 4;
            }
            if (currentPosition < unformattedKey.Length)
            {
                result.Append(unformattedKey.AsSpan(currentPosition));
            }

            return result.ToString().ToLowerInvariant();
        }
    }
}
