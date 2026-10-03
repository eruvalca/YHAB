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
public sealed class ExternalLoginCallbackTests
{
    [Fact]
    public async Task ProviderErrorRedirectsBeforeLookingUpOrUsingExternalIdentityAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ExternalLogin?remoteError=Access%20denied");

        account.Render<ExternalLogin>(context);

        navigation.Uri.ShouldBe("http://localhost/Account/Login");
        account.StatusCookie.ShouldContain("Error from external provider: Access denied");
        await account.SignIn.DidNotReceiveWithAnyArgs().GetExternalLoginInfoAsync(default);
        await account.SignIn.DidNotReceiveWithAnyArgs().ExternalLoginSignInAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task MissingExternalIdentityRedirectsAndCannotCreateAccountOnSubmitAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var component = account.Render<ExternalLogin>(context);
        account.StatusCookie.ShouldContain("Error loading external login information.");
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Login");
        account.StatusCookie.ShouldContain("Error loading external login information during confirmation.");
        await account.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().ExternalLoginSignInAsync(default!, default!, default, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?Action=logincallback")]
    public async Task DirectGetWithoutExactCallbackActionRedirectsWithoutSignInAsync(string query)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Http.Request.Method = HttpMethods.Get;
        account.SignIn.GetExternalLoginInfoAsync().Returns(CreateLogin());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ExternalLogin" + query);

        account.Render<ExternalLogin>(context);

        navigation.Uri.ShouldBe("http://localhost/Account/Login");
        await account.SignIn.DidNotReceiveWithAnyArgs().ExternalLoginSignInAsync(default!, default!, default, default);
    }

    [Theory]
    [InlineData("Success", "http://localhost/events")]
    [InlineData("TwoFactor", "http://localhost/Account/LoginWith2fa?returnUrl=%2Fevents&rememberMe=False")]
    [InlineData("LockedOut", "http://localhost/Account/Lockout")]
    public async Task RecognizedExternalIdentityFollowsSignInOutcomeAsync(string outcome, string destination)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var logger = context.CaptureLogs<ExternalLogin>();
        account.Http.Request.Method = HttpMethods.Get;
        account.SignIn.GetExternalLoginInfoAsync().Returns(CreateLogin());
        account.SignIn.ExternalLoginSignInAsync("Provider", "external-key", false, true).Returns(outcome switch
        {
            "Success" => SignInResult.Success,
            "TwoFactor" => SignInResult.TwoFactorRequired,
            _ => SignInResult.LockedOut,
        });
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ExternalLogin?Action=LoginCallback&returnUrl=%2Fevents");

        account.Render<ExternalLogin>(context);

        navigation.Uri.ShouldBe(destination);
        await account.SignIn.Received(1).ExternalLoginSignInAsync("Provider", "external-key", false, true);
        await account.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!);
        logger.GetLoggedEventIds().Contains(1009).ShouldBe(string.Equals(outcome, "Success", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnrecognizedIdentityWithoutEmailOffersBlankRequiredEmailFieldAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Http.Request.Method = HttpMethods.Get;
        account.SignIn.GetExternalLoginInfoAsync().Returns(CreateLogin());
        account.SignIn.ExternalLoginSignInAsync("Provider", "external-key", false, true).Returns(SignInResult.Failed);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ExternalLogin?Action=LoginCallback");
        var component = account.Render<ExternalLogin>(context);

        await component.Find("form").SubmitAsync();

        component.Find("input[name='Input.Email']").GetAttribute("value").ShouldBeNullOrEmpty();
        component.Find(".validation-message").TextContent.ShouldContain("Email");
        await account.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }

    private static ExternalLoginInfo CreateLogin() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), "Provider", "external-key", "Provider");
}
