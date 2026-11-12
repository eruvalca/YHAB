using Microsoft.AspNetCore.Identity;
using YHAB.Data;

namespace YHAB.Features.Account.Models;

internal union RegistrationOutcome(RegistrationOutcome.Created, RegistrationOutcome.CreationRejected)
{
    internal sealed record Created(ApplicationUser User);
    internal sealed record CreationRejected(IReadOnlyList<IdentityError> Errors);
}
