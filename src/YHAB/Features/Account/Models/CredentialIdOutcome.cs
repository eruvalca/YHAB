using System.Buffers.Text;
using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class CredentialIdOutcome : OneOfBase<CredentialIdOutcome.DecodedCredentialId, CredentialIdOutcome.InvalidCredentialId>
{
    internal sealed record DecodedCredentialId(byte[] Bytes);
    internal readonly record struct InvalidCredentialId;

    public static CredentialIdOutcome Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new InvalidCredentialId();
        }
        try
        {
            var bytes = Base64Url.DecodeFromChars(value);
            return bytes.Length == 0 ? new InvalidCredentialId() : new DecodedCredentialId(bytes);
        }
        catch (FormatException)
        {
            return new InvalidCredentialId();
        }
    }
}
