using Microsoft.AspNetCore.Identity;

namespace YHAB.Features.Account.Models;

internal union EmailChangeOutcome(EmailChangeOutcome.Changed, EmailChangeOutcome.EmailChangeRejected, EmailChangeOutcome.UsernameChangeRejected)
{
    internal readonly record struct Changed;
    internal sealed record EmailChangeRejected(IReadOnlyList<IdentityError> Errors);
    internal sealed record UsernameChangeRejected(IReadOnlyList<IdentityError> Errors);
}
