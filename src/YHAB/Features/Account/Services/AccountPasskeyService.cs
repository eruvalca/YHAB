using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Services;

internal sealed class AccountPasskeyService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
{
    public const int MaxPasskeyCount = 100;

    public async Task<AddPasskeyOutcome> AddAsync(ApplicationUser user, string credentialJson, int currentPasskeyCount)
    {
        if (currentPasskeyCount >= MaxPasskeyCount)
        {
            return new AddPasskeyOutcome.LimitReached();
        }
        var attestation = await signInManager.PerformPasskeyAttestationAsync(credentialJson);
        if (!attestation.Succeeded)
        {
            return new AddPasskeyOutcome.AttestationRejected(attestation.Failure.Message);
        }
        var result = await userManager.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        return result.Succeeded
            ? new AddPasskeyOutcome.Added(attestation.Passkey.CredentialId)
            : new AddPasskeyOutcome.PersistenceRejected(result.Errors.ToArray());
    }

    public Task<PasskeyLookupOutcome> FindAsync(ApplicationUser user, string? credentialId) =>
        CredentialIdOutcome.Decode(credentialId) switch
        {
            CredentialIdOutcome.DecodedCredentialId decoded => FindPasskeyAsync(user, decoded),
            CredentialIdOutcome.InvalidCredentialId invalid => Task.FromResult<PasskeyLookupOutcome>(invalid),
        };

    private async Task<PasskeyLookupOutcome> FindPasskeyAsync(ApplicationUser user, CredentialIdOutcome.DecodedCredentialId decoded)
    {
        var passkey = await userManager.GetPasskeyAsync(user, decoded.Bytes);
        return passkey is null ? new PasskeyLookupOutcome.NotFound() : new PasskeyLookupOutcome.Found(passkey);
    }
}
