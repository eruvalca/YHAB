using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class TwoFactorSettingsTests
{
    [Theory]
    [InlineData(0, "error", "You have no recovery codes left.")]
    [InlineData(1, "error", "You have 1 recovery code left.")]
    [InlineData(2, "warning", "You have 2 recovery codes left.")]
    [InlineData(3, "warning", "You have 3 recovery codes left.")]
    public async Task LowRecoveryCodeCountShowsCorrectSeverityAndRemainingCountAsync(int remaining, string kind, string message)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        account.Users.CountRecoveryCodesAsync(user).Returns(remaining);

        var component = account.Render<TwoFactorAuthentication>(context);

        component.Find($".notice[data-kind='{kind}'] strong").TextContent.ShouldBe(message);
        component.Find(".notice a").GetAttribute("href").ShouldBe("Account/Manage/GenerateRecoveryCodes");
        component.Find("a[href='Account/Manage/Disable2fa']").TextContent.ShouldBe("Disable 2FA");
        await account.Users.Received(1).CountRecoveryCodesAsync(user);
    }

    [Fact]
    public async Task FourRecoveryCodesNeedNoWarningAndUnrememberedBrowserHasNoForgetActionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        account.Users.CountRecoveryCodesAsync(user).Returns(4);

        var component = account.Render<TwoFactorAuthentication>(context);

        component.FindAll(".notice, form").ShouldBeEmpty();
        component.Find("a[href='Account/Manage/GenerateRecoveryCodes']").TextContent.ShouldBe("Reset recovery codes");
        component.Find("a[href='Account/Manage/Disable2fa']").TextContent.ShouldBe("Disable 2FA");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledTwoFactorOffersAuthenticatorSetupWithoutRecoveryOrDisableActionsAsync(bool hasKey)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetAuthenticatorKeyAsync(user).Returns(hasKey ? "existing-key" : null);
        account.SignIn.IsTwoFactorClientRememberedAsync(user).Returns(true);

        var component = account.Render<TwoFactorAuthentication>(context);

        component.FindAll(".notice, form, a[href='Account/Manage/Disable2fa'], a[href='Account/Manage/GenerateRecoveryCodes']").ShouldBeEmpty();
        component.Find("a[href='Account/Manage/EnableAuthenticator']").TextContent.ShouldBe(hasKey ? "Set up authenticator app" : "Add authenticator app");
        component.FindAll("a[href='Account/Manage/ResetAuthenticator']").Count.ShouldBe(hasKey ? 1 : 0);
    }

    [Fact]
    public async Task MissingTrackingConsentHidesAllTwoFactorActionsAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var consent = Substitute.For<ITrackingConsentFeature>();
        consent.CanTrack.Returns(false);
        account.Http.Features.Set(consent);
        account.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        account.Users.GetAuthenticatorKeyAsync(user).Returns("existing-key");
        account.SignIn.IsTwoFactorClientRememberedAsync(user).Returns(true);

        var component = account.Render<TwoFactorAuthentication>(context);

        component.Find(".notice[data-kind='error'] strong").TextContent.ShouldBe("Privacy and cookie policy have not been accepted.");
        component.FindAll("a, form").ShouldBeEmpty();
        await account.SignIn.DidNotReceive().ForgetTwoFactorClientAsync();
    }

    [Fact]
    public async Task RememberedBrowserCanBeForgottenWithConfirmationAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var consent = Substitute.For<ITrackingConsentFeature>();
        consent.CanTrack.Returns(true);
        account.Http.Features.Set(consent);
        account.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        account.SignIn.IsTwoFactorClientRememberedAsync(user).Returns(true);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/TwoFactorAuthentication");
        var component = account.Render<TwoFactorAuthentication>(context);
        component.Find("button").TextContent.ShouldBe("Forget this browser");

        await component.Find("form").SubmitAsync();

        await account.SignIn.Received(1).ForgetTwoFactorClientAsync();
        account.StatusCookie.ShouldContain("The current browser has been forgotten. When you login again from this browser you will be prompted for your 2fa code.");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/TwoFactorAuthentication");
        await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
    }

    [Fact]
    public async Task MissingUserRedirectsWithoutReadingTwoFactorSecretsAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        account.Render<TwoFactorAuthentication>(context);

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/InvalidUser");
        await account.Users.DidNotReceiveWithAnyArgs().GetAuthenticatorKeyAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().CountRecoveryCodesAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().IsTwoFactorClientRememberedAsync(default!);
    }
}
