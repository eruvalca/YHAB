using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using YHAB.Data;

namespace YHAB.ComponentTests.Features.Account;

internal sealed class AccountTestContext(
    DefaultHttpContext http,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IEmailSender<ApplicationUser> emails)
{
    public DefaultHttpContext Http { get; } = http;
    public UserManager<ApplicationUser> Users { get; } = users;
    public SignInManager<ApplicationUser> SignIn { get; } = signIn;
    public IEmailSender<ApplicationUser> Emails { get; } = emails;

    public IRenderedComponent<TComponent> Render<TComponent>(BunitContext context)
        where TComponent : IComponent => context.Render<TComponent>(parameters => parameters.AddCascadingValue<HttpContext>(Http));

    public ApplicationUser Authenticate()
    {
        var user = new ApplicationUser { Email = "member@example.test" };
        Users.GetUserAsync(Http.User).Returns(user);
        Users.GetUserIdAsync(user).Returns(user.Id);
        return user;
    }

    public string StatusCookie => Uri.UnescapeDataString(Http.Response.Headers.SetCookie.ToString());
}
