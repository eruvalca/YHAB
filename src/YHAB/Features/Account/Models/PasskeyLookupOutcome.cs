using Microsoft.AspNetCore.Identity;

namespace YHAB.Features.Account.Models;

internal union PasskeyLookupOutcome(PasskeyLookupOutcome.Found, CredentialIdOutcome.InvalidCredentialId, PasskeyLookupOutcome.NotFound)
{
    internal sealed record Found(UserPasskeyInfo Passkey);
    internal readonly record struct NotFound;
}
