using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using YHAB.Data;

namespace YHAB.Features.Account.Services;

// Identity's public manager methods have no token parameter. Its protected hook forwards
// this explicitly scoped token to stores without retaining an interactive circuit's HttpContext.
internal sealed class CancellableUserManager(
    IUserStore<ApplicationUser> store,
    IOptions<IdentityOptions> options,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    ILookupNormalizer normalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<CancellableUserManager> logger,
    IdentityCancellation cancellation)
    : UserManager<ApplicationUser>(store, options, passwordHasher, userValidators, passwordValidators, normalizer, errors, services, logger)
{
    protected override CancellationToken CancellationToken => cancellation.Token;
}
