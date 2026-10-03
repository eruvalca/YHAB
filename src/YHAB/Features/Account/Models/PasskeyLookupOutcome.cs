using Microsoft.AspNetCore.Identity;
using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class PasskeyLookupOutcome : OneOfBase<PasskeyLookupOutcome.Found, CredentialIdOutcome.InvalidCredentialId, PasskeyLookupOutcome.NotFound>
{
    internal sealed record Found(UserPasskeyInfo Passkey);
    internal readonly record struct NotFound;
}
