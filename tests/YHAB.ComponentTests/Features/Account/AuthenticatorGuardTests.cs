using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class AuthenticatorGuardTests
{
    [Fact]
    public async Task MissingUserCannotLoadAuthenticatorKeyOrSubmitVerificationAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        var component = account.Render<EnableAuthenticator>(context);

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/InvalidUser");
        component.FindAll("form, kbd, [data-url]").ShouldBeEmpty();
        await account.Users.DidNotReceiveWithAnyArgs().GetAuthenticatorKeyAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().VerifyTwoFactorTokenAsync(default!, default!, default!);
    }

    [Fact]
    public async Task MissingUserCannotDisableTwoFactorAfterInitializationRedirectAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var component = account.Render<Disable2fa>(context);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        navigation.NavigateTo("Account/Manage/Disable2fa");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        await account.Users.DidNotReceiveWithAnyArgs().GetTwoFactorEnabledAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
    }

    [Fact]
    public async Task MissingUserCannotGenerateCodesAfterInitializationRedirectAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var component = account.Render<GenerateRecoveryCodes>(context);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        navigation.NavigateTo("Account/Manage/GenerateRecoveryCodes");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        await account.Users.DidNotReceiveWithAnyArgs().GetTwoFactorEnabledAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
        component.FindAll(".recovery-code").ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingUserCannotResetAuthenticatorOrRefreshSignInAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var component = account.Render<ResetAuthenticator>(context);

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/InvalidUser");
        await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
        await account.Users.DidNotReceiveWithAnyArgs().ResetAuthenticatorKeyAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task EnabledUserCanOpenDisablePageWithoutMutatingTwoFactorAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Http.Request.Method = HttpMethods.Get;
        account.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/Disable2fa");

        var component = account.Render<Disable2fa>(context);

        component.Find("form button[type='submit']").TextContent.ShouldBe("Disable 2FA");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/Disable2fa");
        account.StatusCookie.ShouldBeEmpty();
        await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
    }

    [Fact]
    public async Task ProvisioningUriEscapesEmailSeparatorsWithoutAddingQueryParametersAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetAuthenticatorKeyAsync(user).Returns("ABCDEFGHIJKL");
        account.Users.GetEmailAsync(user).Returns("member+tag&issuer=other@example.test");

        var component = account.Render<EnableAuthenticator>(context);

        component.Find("[data-url]").GetAttribute("data-url").ShouldBe(
            "otpauth://totp/Microsoft.AspNetCore.Identity.UI:member%2Btag%26issuer%3Dother@example.test?secret=ABCDEFGHIJKL&issuer=Microsoft.AspNetCore.Identity.UI&digits=6");
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12345678")]
    public async Task InvalidCodeLengthDoesNotAttemptAuthenticatorVerificationAsync(string code)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetAuthenticatorKeyAsync(user).Returns("ABCDEFGHIJKL");
        account.Users.GetEmailAsync(user).Returns("member@example.test");
        var component = account.Render<EnableAuthenticator>(context);
        await component.Find("input[name='Input.Code']").ChangeAsync(new ChangeEventArgs { Value = code });

        await component.Find("form").SubmitAsync();

        component.Find(".validation-message").TextContent.ShouldBe(string.IsNullOrEmpty(code)
            ? "The Verification Code field is required."
            : "The Verification Code must be at least 6 and at max 7 characters long.");
        await account.Users.DidNotReceiveWithAnyArgs().VerifyTwoFactorTokenAsync(default!, default!, default!);
        await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
    }
}
