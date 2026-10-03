using Microsoft.AspNetCore.Identity;
using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class EmailChangeOutcome : OneOfBase<EmailChangeOutcome.Changed, EmailChangeOutcome.EmailChangeRejected, EmailChangeOutcome.UsernameChangeRejected>
{
    internal readonly record struct Changed;
    internal sealed record EmailChangeRejected(IReadOnlyList<IdentityError> Errors);
    internal sealed record UsernameChangeRejected(IReadOnlyList<IdentityError> Errors);
}
