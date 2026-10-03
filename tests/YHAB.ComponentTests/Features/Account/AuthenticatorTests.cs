using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
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
public sealed class AuthenticatorTests
{
    [Fact]
    public async Task SetupPreservesFormattedKeyAndProvisioningUriAsync()
    {
        await using var context = new BunitContext();
        var (account, _) = ConfigureAuthenticator(context);

        var component = account.Render<EnableAuthenticator>(context);

        component.Find("kbd").TextContent.ShouldBe("abcd efgh ijkl");
        component.Find("[data-url]").GetAttribute("data-url")
            .ShouldBe("otpauth://totp/Microsoft.AspNetCore.Identity.UI:member@example.test?secret=ABCDEFGHIJKL&issuer=Microsoft.AspNetCore.Identity.UI&digits=6");
    }

    [Fact]
    public async Task FailedSetupShowsErrorAndCannotEnableAuthenticatorAsync()
    {
        await using var context = new BunitContext();
        var (account, user) = ConfigureAuthenticator(context);
        account.Users.GetAuthenticatorKeyAsync(user).Returns((string?)null);
        account.Users.ResetAuthenticatorKeyAsync(user).Returns(IdentityResult.Failed());

        var component = account.Render<EnableAuthenticator>(context);

        component.Find(".notice[data-kind='error']").TextContent.ShouldContain("authenticator");
        component.FindAll("form").ShouldBeEmpty();
        await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
    }

    [Theory]
    [InlineData(false, false, "Verification code is invalid")]
    [InlineData(true, false, "could not be enabled")]
    [InlineData(true, true, "recovery codes")]
    public async Task FailedEnablementDisplaysFailureWithoutCodesOrSuccessLogAsync(bool validCode, bool enableSucceeds, string expectedMessage)
    {
        await using var context = new BunitContext();
        var (account, user) = ConfigureAuthenticator(context);
        var logger = context.CaptureLogs<EnableAuthenticator>();
        account.Users.VerifyTwoFactorTokenAsync(user, account.Users.Options.Tokens.AuthenticatorTokenProvider, "123456").Returns(validCode);
        account.Users.SetTwoFactorEnabledAsync(user, true).Returns(enableSucceeds ? IdentityResult.Success : IdentityResult.Failed());
        account.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns((IEnumerable<string>?)null);
        var component = account.Render<EnableAuthenticator>(context);
        await component.Find("input[name='Input.Code']").ChangeAsync(new ChangeEventArgs { Value = "123-456" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldContain(expectedMessage));
        component.FindAll(".recovery-code").ShouldBeEmpty();
        component.FindAll(".notice[data-kind='success']").ShouldBeEmpty();
        logger.GetLoggedEventIds().ShouldNotContain(1015);
        if (!validCode)
        {
            await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
        }
        if (!enableSucceeds)
        {
            await account.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
        }
    }

    [Fact]
    public async Task EnableWithNewRecoveryCodesDisplaysCodesAndSuccessAsync()
    {
        await using var context = new BunitContext();
        var (account, user) = ConfigureAuthenticator(context);
        var logger = context.CaptureLogs<EnableAuthenticator>();
        account.Users.VerifyTwoFactorTokenAsync(user, account.Users.Options.Tokens.AuthenticatorTokenProvider, "123456").Returns(true);
        account.Users.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Success);
        string[] codes = ["first-code", "second-code"];
        account.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns(codes);
        var component = account.Render<EnableAuthenticator>(context);
        await component.Find("input[name='Input.Code']").ChangeAsync(new ChangeEventArgs { Value = "123456" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.FindAll(".recovery-code").Select(element => element.TextContent).ShouldBe(codes));
        component.Find(".notice[data-kind='success']").TextContent.ShouldBe("Your authenticator app has been verified.");
        component.FindAll("form").ShouldBeEmpty();
        logger.GetLoggedEventIds().ShouldContain(1015);
    }

    [Fact]
    public async Task EnableWithExistingCodesRedirectsWithoutReplacingThemAsync()
    {
        await using var context = new BunitContext();
        var (account, user) = ConfigureAuthenticator(context);
        account.Users.VerifyTwoFactorTokenAsync(user, account.Users.Options.Tokens.AuthenticatorTokenProvider, "123456").Returns(true);
        account.Users.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Success);
        account.Users.CountRecoveryCodesAsync(user).Returns(1);
        var component = account.Render<EnableAuthenticator>(context);
        await component.Find("input[name='Input.Code']").ChangeAsync(new ChangeEventArgs { Value = "123456" });

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/TwoFactorAuthentication");
        account.StatusCookie.ShouldContain("Your authenticator app has been verified.");
        await account.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
    }

    [Theory]
    [InlineData(false, "could not be disabled")]
    [InlineData(true, "key could not be reset")]
    public async Task FailedResetExplainsProgressWithoutSuccessLogOrRefreshAsync(bool disabled, string expectedMessage)
    {
        await using var context = new BunitContext();
        var (account, user) = ConfigureAuthenticator(context);
        var logger = context.CaptureLogs<ResetAuthenticator>();
        account.Users.SetTwoFactorEnabledAsync(user, false).Returns(disabled ? IdentityResult.Success : IdentityResult.Failed());
        account.Users.ResetAuthenticatorKeyAsync(user).Returns(IdentityResult.Failed());
        var component = account.Render<ResetAuthenticator>(context);

        await component.Find("form").SubmitAsync();

        account.StatusCookie.ShouldContain(expectedMessage);
        account.StatusCookie.ShouldNotContain("key has been reset");
        logger.GetLoggedEventIds().ShouldNotContain(1017);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        if (!disabled)
        {
            await account.Users.DidNotReceiveWithAnyArgs().ResetAuthenticatorKeyAsync(default!);
        }
    }

    [Fact]
    public async Task SuccessfulResetRefreshesSignInAndNavigatesToSetupAsync()
    {
        await using var context = new BunitContext();
        var (account, user) = ConfigureAuthenticator(context);
        var logger = context.CaptureLogs<ResetAuthenticator>();
        account.Users.SetTwoFactorEnabledAsync(user, false).Returns(IdentityResult.Success);
        account.Users.ResetAuthenticatorKeyAsync(user).Returns(IdentityResult.Success);
        var component = account.Render<ResetAuthenticator>(context);

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/EnableAuthenticator");
        account.StatusCookie.ShouldContain("key has been reset");
        await account.SignIn.Received(1).RefreshSignInAsync(user);
        logger.GetLoggedEventIds().ShouldContain(1017);
    }

    private static (AccountTestContext Account, ApplicationUser User) ConfigureAuthenticator(BunitContext context)
    {
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetAuthenticatorKeyAsync(user).Returns("ABCDEFGHIJKL");
        account.Users.GetEmailAsync(user).Returns("member@example.test");
        return (account, user);
    }
}
