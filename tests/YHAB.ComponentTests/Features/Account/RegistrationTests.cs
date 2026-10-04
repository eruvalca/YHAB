using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Pages;
using YHAB.Features.Account.Services;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class RegistrationTests
{
    [Fact]
    public async Task RejectedPasswordRegistrationDisplaysIdentityErrorAndSkipsContinuationAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.SupportsUserEmail.Returns(true);
        account.Users.CreateAsync(Arg.Any<ApplicationUser>(), "password").Returns(IdentityResult.Failed(new IdentityError { Description = "Email already registered" }));
        var component = account.Render<Register>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = "password" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldContain("Email already registered"));
        await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().SignInAsync(default!, default(bool), default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulPasswordRegistrationOnlyEmailsWhenConfirmationIsRequiredAsync(bool requireConfirmation)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.SupportsUserEmail.Returns(true);
        account.Users.Options.SignIn.RequireConfirmedAccount = requireConfirmation;
        account.Users.CreateAsync(Arg.Any<ApplicationUser>(), "password").Returns(IdentityResult.Success);
        account.Users.GetUserIdAsync(Arg.Any<ApplicationUser>()).Returns("member");
        account.Users.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Register?returnUrl=%2Fevents");
        var component = account.Render<Register>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = "password" });

        await component.Find("form").SubmitAsync();

        if (requireConfirmation)
        {
            await account.Emails.Received(1).SendConfirmationLinkAsync(Arg.Any<ApplicationUser>(), "member@example.test",
                Arg.Is<string>(link => link.Contains("Account/ConfirmEmail?userId=member", StringComparison.Ordinal)
                    && link.Contains("code=dG9rZW4", StringComparison.Ordinal)
                    && link.Contains("returnUrl=%2Fevents", StringComparison.Ordinal)));
            navigation.Uri.ShouldBe("http://localhost/Account/RegisterConfirmation?email=member%40example.test&returnUrl=%2Fevents");
            await account.SignIn.DidNotReceiveWithAnyArgs().SignInAsync(default!, default(bool), default);
        }
        else
        {
            navigation.Uri.ShouldBe("http://localhost/events");
            await account.SignIn.Received(1).SignInAsync(Arg.Is<ApplicationUser>(user => !user.EmailConfirmed), false, null);
            await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
            await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalRegistrationFailureSkipsEmailAndSignInAndExplainsPartialAccountAsync(bool linkFailed)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.SupportsUserEmail.Returns(true);
        var login = new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity()), "Provider", "key", "Provider");
        account.SignIn.GetExternalLoginInfoAsync().Returns(login);
        var failure = IdentityResult.Failed(new IdentityError { Description = "Operation rejected" });
        account.Users.CreateAsync(Arg.Any<ApplicationUser>()).Returns(linkFailed ? IdentityResult.Success : failure);
        account.Users.AddLoginAsync(Arg.Any<ApplicationUser>(), login).Returns(failure);
        var component = account.Render<ExternalLogin>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldContain("Operation rejected"));
        if (linkFailed)
        {
            component.Find("h1").TextContent.ShouldBe("Finish setting up your account");
            var steps = component.FindAll("ol li");
            steps.Count.ShouldBe(3);
            steps[0].TextContent.ShouldContain("Get help recovering your account");
            steps[1].TextContent.ShouldContain("new password");
            steps[2].TextContent.ShouldContain("External logins");
            component.FindAll("form").ShouldBeEmpty();
        }
        else
        {
            component.Find("h1").TextContent.ShouldBe("Register");
            await account.Users.DidNotReceiveWithAnyArgs().AddLoginAsync(default!, default!);
        }
        await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().SignInAsync(default!, default(bool), default);
        await account.Users.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulExternalRegistrationOnlyEmailsWhenConfirmationIsRequiredAsync(bool requireConfirmation)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.SupportsUserEmail.Returns(true);
        account.Users.Options.SignIn.RequireConfirmedAccount = requireConfirmation;
        var login = new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity()), "Provider", "key", "Provider");
        account.SignIn.GetExternalLoginInfoAsync().Returns(login);
        account.Users.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        account.Users.AddLoginAsync(Arg.Any<ApplicationUser>(), login).Returns(IdentityResult.Success);
        account.Users.GetUserIdAsync(Arg.Any<ApplicationUser>()).Returns("member");
        account.Users.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ExternalLogin?returnUrl=%2Fevents");
        var component = account.Render<ExternalLogin>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });

        await component.Find("form").SubmitAsync();

        await account.Users.Received(1).AddLoginAsync(Arg.Any<ApplicationUser>(), login);
        if (requireConfirmation)
        {
            await account.Emails.Received(1).SendConfirmationLinkAsync(Arg.Any<ApplicationUser>(), "member@example.test",
                "http://localhost/Account/ConfirmEmail?userId=member&amp;code=dG9rZW4");
            navigation.Uri.ShouldBe("http://localhost/Account/RegisterConfirmation?email=member%40example.test");
            await account.SignIn.DidNotReceiveWithAnyArgs().SignInAsync(default!, default(bool), default);
        }
        else
        {
            navigation.Uri.ShouldBe("http://localhost/events");
            await account.SignIn.Received(1).SignInAsync(Arg.Any<ApplicationUser>(), false, "Provider");
            await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
            await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedExternalLinkOffersOwnerRecoveryWithEitherEmailSenderAsync(bool hasEmailSender)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        if (!hasEmailSender)
        {
            context.Services.AddSingleton<IEmailSender<ApplicationUser>>(new IdentityNoOpEmailSender());
        }
        account.Users.SupportsUserEmail.Returns(true);
        var login = new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity()), "Provider", "key", "Provider");
        account.SignIn.GetExternalLoginInfoAsync().Returns(login);
        account.Users.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        account.Users.AddLoginAsync(Arg.Any<ApplicationUser>(), login).Returns(IdentityResult.Failed());
        var component = account.Render<ExternalLogin>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find("a[href='Account/ForgotPassword']").TextContent
            .ShouldBe("Get help recovering your account"));
        component.FindAll("a[href^='Account/RegisterConfirmation']").ShouldBeEmpty();
        await account.SignIn.DidNotReceiveWithAnyArgs().SignInAsync(default!, default(bool), default);

        var recovery = account.Render<ForgotPassword>(context);
        recovery.Markup.ShouldContain("Contact the person who runs this YHAB instance");
        recovery.Markup.ShouldContain("temporary password-reset link");
    }
}
