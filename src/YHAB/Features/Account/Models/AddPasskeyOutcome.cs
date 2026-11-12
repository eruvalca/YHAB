using Microsoft.AspNetCore.Identity;

namespace YHAB.Features.Account.Models;

internal union AddPasskeyOutcome(AddPasskeyOutcome.Added, AddPasskeyOutcome.LimitReached, AddPasskeyOutcome.AttestationRejected, AddPasskeyOutcome.PersistenceRejected)
{
    internal sealed record Added(byte[] CredentialId);
    internal readonly record struct LimitReached;
    internal sealed record AttestationRejected(string Message);
    internal sealed record PersistenceRejected(IReadOnlyList<IdentityError> Errors);
}
