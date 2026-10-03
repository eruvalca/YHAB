using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class TokenDecodeOutcome : OneOfBase<TokenDecodeOutcome.DecodedToken, TokenDecodeOutcome.InvalidToken>
{
    internal sealed record DecodedToken(string Value);
    internal readonly record struct InvalidToken;

    public static TokenDecodeOutcome Decode(string? encoded)
    {
        if (encoded is null)
        {
            return new InvalidToken();
        }
        try
        {
            return new DecodedToken(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded)));
        }
        catch (FormatException)
        {
            return new InvalidToken();
        }
    }
}
