using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class TwoFactorSignInTests
{
    [Theory]
    [InlineData("Failed")]
    [InlineData("NotAllowed")]
    [InlineData("RequiresTwoFactor")]
    public async Task RejectedAuthenticatorUsesGenericCodeFailureAsync(string outcome)
    {
        await using var context = new BunitContext();
        var account = Configure(context);
        account.SignIn.TwoFactorAuthenticatorSignInAsync("123456", false, false).Returns(Rejected(outcome));
        var component = account.Render<LoginWith2fa>(context);
        await component.Find("input[name='Input.TwoFactorCode']").ChangeAsync(new ChangeEventArgs { Value = "123-456" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error: Invalid authenticator code."));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NotAllowed")]
    [InlineData("RequiresTwoFactor")]
    public async Task RejectedRecoveryCodeUsesGenericCodeFailureAsync(string outcome)
    {
        await using var context = new BunitContext();
        var account = Configure(context);
        account.SignIn.TwoFactorRecoveryCodeSignInAsync("123-456").Returns(Rejected(outcome));
        var component = account.Render<LoginWithRecoveryCode>(context);
        await component.Find("input[name='Input.RecoveryCode']").ChangeAsync(new ChangeEventArgs { Value = "123 -456" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error: Invalid recovery code entered."));
    }

    [Fact]
    public async Task AuthenticatorSuccessPreservesRememberSettingsAndReturnUrlAsync()
    {
        await using var context = new BunitContext();
        var account = Configure(context);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/LoginWith2fa?rememberMe=true&returnUrl=%2Fevents");
        account.SignIn.TwoFactorAuthenticatorSignInAsync("123456", true, true).Returns(SignInResult.Success);
        var component = account.Render<LoginWith2fa>(context);
        await component.Find("input[name='Input.TwoFactorCode']").ChangeAsync(new ChangeEventArgs { Value = "123 456" });
        await component.Find("input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true });

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/events");
        await account.SignIn.Received(1).TwoFactorAuthenticatorSignInAsync("123456", true, true);
    }

    [Fact]
    public async Task RecoveryCodeLockoutRedirectsToLockoutAsync()
    {
        await using var context = new BunitContext();
        var account = Configure(context);
        account.SignIn.TwoFactorRecoveryCodeSignInAsync("123-456").Returns(SignInResult.LockedOut);
        var component = account.Render<LoginWithRecoveryCode>(context);
        await component.Find("input[name='Input.RecoveryCode']").ChangeAsync(new ChangeEventArgs { Value = "123-456" });

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Lockout");
    }

    private static AccountTestContext Configure(BunitContext context)
    {
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.SignIn.GetTwoFactorAuthenticationUserAsync().Returns(user);
        return account;
    }

    private static SignInResult Rejected(string outcome) => outcome switch
    {
        "NotAllowed" => SignInResult.NotAllowed,
        "RequiresTwoFactor" => SignInResult.TwoFactorRequired,
        _ => SignInResult.Failed,
    };
}
