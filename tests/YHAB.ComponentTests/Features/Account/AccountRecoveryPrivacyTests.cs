using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Pages;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountRecoveryPrivacyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ForgotPasswordAlwaysNavigatesToSameConfirmationAsync(bool exists, bool confirmed)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByEmailAsync("member@example.test").Returns(exists ? user : null);
        account.Users.IsEmailConfirmedAsync(user).Returns(confirmed);
        account.Users.GeneratePasswordResetTokenAsync(user).Returns("token");
        var component = account.Render<ForgotPassword>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/ForgotPasswordConfirmation");
        if (exists && confirmed)
        {
            await account.Emails.Received(1).SendPasswordResetLinkAsync(user, "member@example.test", Arg.Is<string>(link => link.Contains("code=dG9rZW4", StringComparison.Ordinal)));
        }
        else
        {
            await account.Users.DidNotReceiveWithAnyArgs().GeneratePasswordResetTokenAsync(default!);
            await account.Emails.DidNotReceiveWithAnyArgs().SendPasswordResetLinkAsync(default!, default!, default!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResendConfirmationAlwaysShowsSamePublicMessageAsync(bool exists)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByEmailAsync("member@example.test").Returns(exists ? user : null);
        account.Users.GetUserIdAsync(user).Returns("member");
        account.Users.GenerateEmailConfirmationTokenAsync(user).Returns("token");
        var component = account.Render<ResendEmailConfirmation>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='success']").TextContent.ShouldBe("Verification email sent. Please check your email."));
        if (exists)
        {
            await account.Emails.Received(1).SendConfirmationLinkAsync(user, "member@example.test", Arg.Any<string>());
        }
        else
        {
            await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
            await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordResetUsesSameConfirmationForMissingUserAndSuccessAsync(bool exists)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByEmailAsync("member@example.test").Returns(exists ? user : null);
        account.Users.ResetPasswordAsync(user, "token", "password").Returns(IdentityResult.Success);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ResetPassword?code=dG9rZW4");
        var component = account.Render<ResetPassword>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = "password" });

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/ResetPasswordConfirmation");
        if (exists)
        {
            await account.Users.Received(1).ResetPasswordAsync(user, "token", "password");
        }
        else
        {
            await account.Users.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default!);
        }
    }
}
