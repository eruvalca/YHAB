using Microsoft.AspNetCore.Identity;
using OneOf;
using YHAB.Data;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class ExternalRegistrationOutcome : OneOfBase<RegistrationOutcome.Created, RegistrationOutcome.CreationRejected, ExternalRegistrationOutcome.ExternalLoginLinkFailed>
{
    internal sealed record ExternalLoginLinkFailed(ApplicationUser User, IReadOnlyList<IdentityError> Errors);
}
