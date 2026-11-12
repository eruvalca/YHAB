using Microsoft.AspNetCore.Identity;
using YHAB.Data;

namespace YHAB.Features.Account.Models;

internal union ExternalRegistrationOutcome(RegistrationOutcome.Created, RegistrationOutcome.CreationRejected, ExternalRegistrationOutcome.ExternalLoginLinkFailed)
{
    internal sealed record ExternalLoginLinkFailed(ApplicationUser User, IReadOnlyList<IdentityError> Errors);
}
