using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ExternalLoginTests
{
    [Fact]
    public async Task NotAllowedRedirectsToGenericLoginFailureWithoutPrefillingRegistrationAsync()
    {
        await using var context = new BunitContext();
        var account = ConfigureCallback(context, SignInResult.NotAllowed);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var component = account.Render<ExternalLogin>(context);

        await component.WaitForAssertionAsync(() => navigation.Uri.ShouldBe("http://localhost/Account/Login"));
        Uri.UnescapeDataString(account.Http.Response.Headers.SetCookie.ToString()).ShouldContain("Error: Invalid login attempt.");
        component.Find("input[name='Input.Email']").GetAttribute("value").ShouldBeNullOrEmpty();
        await account.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }

    [Fact]
    public async Task OrdinaryFailureOffersRegistrationUsingProviderEmailAsync()
    {
        await using var context = new BunitContext();
        var account = ConfigureCallback(context, SignInResult.Failed);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var component = account.Render<ExternalLogin>(context);

        await component.WaitForAssertionAsync(() => component.Find("input[name='Input.Email']").GetAttribute("value").ShouldBe("member@example.test"));
        navigation.Uri.ShouldContain("Account/ExternalLogin");
        account.Http.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    private static AccountTestContext ConfigureCallback(BunitContext context, SignInResult result)
    {
        var account = context.ConfigureAccount();
        account.Http.Request.Method = HttpMethods.Get;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "member@example.test")], "Provider"));
        account.SignIn.GetExternalLoginInfoAsync().Returns(new ExternalLoginInfo(principal, "Provider", "key", "Provider"));
        account.SignIn.ExternalLoginSignInAsync("Provider", "key", false, true).Returns(result);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ExternalLogin?Action=LoginCallback");
        return account;
    }
}
