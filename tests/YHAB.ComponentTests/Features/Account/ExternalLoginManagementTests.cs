using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ExternalLoginManagementTests
{
    [Fact]
    public async Task AvailableProvidersExcludeLinkedNamesUsingOrdinalComparisonAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetLoginsAsync(user).Returns([new UserLoginInfo("Provider", "linked-key", "Linked provider")]);
        account.SignIn.GetExternalAuthenticationSchemesAsync().Returns([
            new AuthenticationScheme("Provider", "Linked provider", typeof(IAuthenticationHandler)),
            new AuthenticationScheme("provider", "Different case", typeof(IAuthenticationHandler)),
            new AuthenticationScheme("Other", "Other provider", typeof(IAuthenticationHandler))]);

        var component = account.Render<ExternalLogins>(context);

        component.Find("table td").TextContent.ShouldBe("Linked provider");
        component.FindAll("button[name='Provider']").Select(button => button.GetAttribute("value"))
            .ShouldBe(["provider", "Other"]);
        component.Find("form[action]").GetAttribute("action").ShouldBe("Account/Manage/LinkExternalLogin");
        component.FindAll("table button").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, true)]
    [InlineData(true, 1, true)]
    public async Task RemoveActionsRequirePasswordOrAnotherLinkedLoginAsync(bool hasPassword, int loginCount, bool canRemove)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var store = Substitute.For<IUserPasswordStore<ApplicationUser>>();
        store.GetPasswordHashAsync(user, account.Http.RequestAborted).Returns(hasPassword ? "password-hash" : null);
        context.Services.AddSingleton<IUserStore<ApplicationUser>>(store);
        var logins = Enumerable.Range(1, loginCount)
            .Select(index => new UserLoginInfo($"Provider{index}", $"key{index}", $"Provider {index}")).ToList();
        account.Users.GetLoginsAsync(user).Returns(logins);

        var component = account.Render<ExternalLogins>(context);

        component.FindAll("table button").Count.ShouldBe(canRemove ? loginCount : 0);
        if (canRemove)
        {
            component.FindAll("input[name='LoginProvider']").Select(input => input.GetAttribute("value"))
                .ShouldBe(logins.Select(login => login.LoginProvider));
            component.FindAll("input[name='ProviderKey']").Select(input => input.GetAttribute("value"))
                .ShouldBe(logins.Select(login => login.ProviderKey));
        }
        await store.Received(1).GetPasswordHashAsync(user, account.Http.RequestAborted);
    }

    [Fact]
    public async Task EmptyProviderListsHideBothLoginSectionsAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetLoginsAsync(user).Returns([]);

        var component = account.Render<ExternalLogins>(context);

        component.FindAll("table, form, h3, h4").ShouldBeEmpty();
        await account.Users.Received(1).GetLoginsAsync(user);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingLoginUsesPostedIdentityAndRefreshesOnlyAfterSuccessAsync(bool succeeds)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetLoginsAsync(user).Returns([
            new UserLoginInfo("First", "first-key", "First"), new UserLoginInfo("Second", "second-key", "Second")]);
        account.Users.RemoveLoginAsync(user, "Second", "second-key")
            .Returns(succeeds ? IdentityResult.Success : IdentityResult.Failed());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/ExternalLogins");
        var component = account.Render<ExternalLogins>(context);
        component.Instance.SetFormValue("LoginProvider", "Second");
        component.Instance.SetFormValue("ProviderKey", "second-key");

        await component.FindAll("table form")[1].SubmitAsync();

        await account.Users.Received(1).RemoveLoginAsync(user, "Second", "second-key");
        account.StatusCookie.ShouldContain(succeeds ? "The external login was removed." : "Error: The external login was not removed.");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/ExternalLogins");
        if (succeeds)
        {
            await account.SignIn.Received(1).RefreshSignInAsync(user);
            Received.InOrder(() =>
            {
                _ = account.Users.RemoveLoginAsync(user, "Second", "second-key");
                _ = account.SignIn.RefreshSignInAsync(user);
            });
        }
        else
        {
            await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        }
    }

    [Fact]
    public async Task MissingCallbackInformationReportsFailureWithoutLinkingAsync()
    {
        await using var context = new BunitContext();
        var account = ConfigureCallback(context);
        var authentication = context.Services.GetRequiredService<IAuthenticationService>();

        account.Render<ExternalLogins>(context);

        account.StatusCookie.ShouldContain("Error: Could not load external login info.");
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/ExternalLogins");
        await account.SignIn.Received(1).GetExternalLoginInfoAsync("member-id");
        await account.Users.DidNotReceiveWithAnyArgs().AddLoginAsync(default!, default!);
        await authentication.DidNotReceiveWithAnyArgs().SignOutAsync(default!, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackLinksLoginAndClearsExternalCookieOnlyOnSuccessAsync(bool succeeds)
    {
        await using var context = new BunitContext();
        var account = ConfigureCallback(context);
        var user = await account.Users.GetUserAsync(account.Http.User);
        var info = new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity()), "Provider", "external-key", "Provider");
        account.SignIn.GetExternalLoginInfoAsync("member-id").Returns(info);
        account.Users.AddLoginAsync(user!, info).Returns(succeeds ? IdentityResult.Success : IdentityResult.Failed());
        var authentication = context.Services.GetRequiredService<IAuthenticationService>();

        account.Render<ExternalLogins>(context);

        await account.Users.Received(1).AddLoginAsync(user!, info);
        account.StatusCookie.ShouldContain(succeeds
            ? "The external login was added."
            : "Error: The external login was not added. External logins can only be associated with one account.");
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/ExternalLogins");
        if (succeeds)
        {
            await authentication.Received(1).SignOutAsync(account.Http, IdentityConstants.ExternalScheme, null);
            Received.InOrder(() =>
            {
                _ = account.Users.AddLoginAsync(user!, info);
                _ = authentication.SignOutAsync(account.Http, IdentityConstants.ExternalScheme, null);
            });
        }
        else
        {
            await authentication.DidNotReceiveWithAnyArgs().SignOutAsync(default!, default, default);
        }
    }

    [Theory]
    [InlineData("POST", "LinkLoginCallback")]
    [InlineData("GET", "linklogincallback")]
    public async Task CallbackRequiresGetAndExactActionAsync(string method, string action)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetLoginsAsync(user).Returns([]);
        account.Http.Request.Method = method;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"Account/Manage/ExternalLogins?Action={action}");

        account.Render<ExternalLogins>(context);

        await account.SignIn.DidNotReceiveWithAnyArgs().GetExternalLoginInfoAsync(default);
        await account.Users.DidNotReceiveWithAnyArgs().AddLoginAsync(default!, default!);
        account.StatusCookie.ShouldBeEmpty();
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe($"http://localhost/Account/Manage/ExternalLogins?Action={action}");
    }

    [Fact]
    public async Task MissingUserRedirectsBeforeLoadingOrChangingLinkedLoginsAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        account.Render<ExternalLogins>(context);

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/InvalidUser");
        await account.Users.DidNotReceiveWithAnyArgs().GetLoginsAsync(default!);
        await account.SignIn.DidNotReceive().GetExternalAuthenticationSchemesAsync();
        await account.Users.DidNotReceiveWithAnyArgs().AddLoginAsync(default!, default!);
    }

    private static AccountTestContext ConfigureCallback(BunitContext context)
    {
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetUserIdAsync(user).Returns("member-id");
        account.Users.GetLoginsAsync(user).Returns([]);
        context.Services.AddSingleton(Substitute.For<IAuthenticationService>());
        account.Http.Request.Method = HttpMethods.Get;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/Manage/ExternalLogins?Action=LinkLoginCallback");
        return account;
    }
}
