using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using YHAB.Data;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

internal sealed class IdentityTestContext(
    IUserEmailStore<ApplicationUser> store,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IDisposable
{
    public IUserEmailStore<ApplicationUser> Store { get; } = store;
    public UserManager<ApplicationUser> Users { get; } = userManager;
    public SignInManager<ApplicationUser> SignIn { get; } = signInManager;
    public IdentityCancellation Cancellation { get; } = new();

    public static IdentityTestContext Create()
    {
        var store = Substitute.For<IUserEmailStore<ApplicationUser>>();
        var options = Options.Create(new IdentityOptions());
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store, options, new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(), NullLogger<UserManager<ApplicationUser>>.Instance);
        var signIn = Substitute.For<SignInManager<ApplicationUser>>(
            users, new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>(), options,
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(), Substitute.For<IUserConfirmation<ApplicationUser>>());
        return new(store, users, signIn);
    }

    public void Dispose() => Users.Dispose();
}
