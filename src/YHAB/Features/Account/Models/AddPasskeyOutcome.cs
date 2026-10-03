using Microsoft.AspNetCore.Identity;
using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class AddPasskeyOutcome : OneOfBase<AddPasskeyOutcome.Added, AddPasskeyOutcome.LimitReached, AddPasskeyOutcome.AttestationRejected, AddPasskeyOutcome.PersistenceRejected>
{
    internal sealed record Added(byte[] CredentialId);
    internal readonly record struct LimitReached;
    internal sealed record AttestationRejected(string Message);
    internal sealed record PersistenceRejected(IReadOnlyList<IdentityError> Errors);
}
