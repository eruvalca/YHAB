using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Authentication;
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
public sealed class LoginOutcomeTests
{
    [Theory]
    [InlineData("GET", 1)]
    [InlineData("POST", 0)]
    public async Task OpeningLoginClearsExternalCookieOnlyOnGetAsync(string method, int expectedSignOuts)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var authentication = Substitute.For<IAuthenticationService>();
        context.Services.AddSingleton(authentication);
        account.Http.Request.Method = method;

        account.Render<Login>(context);

        await authentication.Received(expectedSignOuts).SignOutAsync(account.Http, IdentityConstants.ExternalScheme, null);
        await authentication.ReceivedWithAnyArgs(expectedSignOuts).SignOutAsync(default!, default, default);
        await account.SignIn.DidNotReceive().SignOutAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordSuccessAndLockoutNavigateAndLogCorrectOutcomeAsync(bool lockedOut)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var logger = context.CaptureLogs<Login>();
        account.SignIn.PasswordSignInAsync("member@example.test", "password", true, false)
            .Returns(lockedOut ? SignInResult.LockedOut : SignInResult.Success);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Login?returnUrl=%2Fevents");
        var component = account.Render<Login>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        await component.Find("input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true });

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe(lockedOut ? "http://localhost/Account/Lockout" : "http://localhost/events");
        await account.SignIn.Received(1).PasswordSignInAsync("member@example.test", "password", true, false);
        logger.GetLoggedEventIds().ShouldBe([lockedOut ? 1002 : 1001]);
    }

    [Fact]
    public async Task AuthenticatorLockoutRedirectsAndLogsWithoutSuccessAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.SignIn.GetTwoFactorAuthenticationUserAsync().Returns(user);
        account.SignIn.TwoFactorAuthenticatorSignInAsync("123456", false, false).Returns(SignInResult.LockedOut);
        var logger = context.CaptureLogs<LoginWith2fa>();
        var component = account.Render<LoginWith2fa>(context);
        await component.Find("input[name='Input.TwoFactorCode']").ChangeAsync(new ChangeEventArgs { Value = "123456" });

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Lockout");
        await account.SignIn.Received(1).TwoFactorAuthenticatorSignInAsync("123456", false, false);
        logger.GetLoggedEventIds().ShouldBe([1004]);
    }

    [Fact]
    public async Task RecoveryCodeSuccessNormalizesSpacesAndReturnsToRequestedPageAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.SignIn.GetTwoFactorAuthenticationUserAsync().Returns(user);
        account.SignIn.TwoFactorRecoveryCodeSignInAsync("abc-123").Returns(SignInResult.Success);
        var logger = context.CaptureLogs<LoginWithRecoveryCode>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/LoginWithRecoveryCode?returnUrl=%2Fevents");
        var component = account.Render<LoginWithRecoveryCode>(context);
        await component.Find("input[name='Input.RecoveryCode']").ChangeAsync(new ChangeEventArgs { Value = " abc -123 " });

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/events");
        await account.SignIn.Received(1).TwoFactorRecoveryCodeSignInAsync("abc-123");
        logger.GetLoggedEventIds().ShouldBe([1006]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12345678")]
    public async Task InvalidAuthenticatorInputPreventsSignInAsync(string code)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.SignIn.GetTwoFactorAuthenticationUserAsync().Returns(user);
        var component = account.Render<LoginWith2fa>(context);
        await component.Find("input[name='Input.TwoFactorCode']").ChangeAsync(new ChangeEventArgs { Value = code });

        await component.Find("form").SubmitAsync();

        component.Find(".validation-message").TextContent.ShouldContain("Authenticator code");
        await account.SignIn.DidNotReceiveWithAnyArgs().TwoFactorAuthenticatorSignInAsync(default!, default, default);
    }

    [Fact]
    public async Task EmptyRecoveryCodePreventsSignInAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.SignIn.GetTwoFactorAuthenticationUserAsync().Returns(user);
        var component = account.Render<LoginWithRecoveryCode>(context);

        await component.Find("form").SubmitAsync();

        component.Find(".validation-message").TextContent.ShouldContain("Recovery Code");
        await account.SignIn.DidNotReceiveWithAnyArgs().TwoFactorRecoveryCodeSignInAsync(default!);
    }

    [Fact]
    public async Task AuthenticatorRequiresPendingTwoFactorUserAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        Should.Throw<InvalidOperationException>(() => account.Render<LoginWith2fa>(context))
            .Message.ShouldBe("Unable to load two-factor authentication user.");

        await account.SignIn.DidNotReceiveWithAnyArgs().TwoFactorAuthenticatorSignInAsync(default!, default, default);
    }

    [Fact]
    public async Task RecoveryCodeRequiresPendingTwoFactorUserAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        Should.Throw<InvalidOperationException>(() => account.Render<LoginWithRecoveryCode>(context))
            .Message.ShouldBe("Unable to load two-factor authentication user.");

        await account.SignIn.DidNotReceiveWithAnyArgs().TwoFactorRecoveryCodeSignInAsync(default!);
    }
}
