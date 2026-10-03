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
public sealed class ConfirmationTests
{
    [Theory]
    [InlineData(false, "Error confirming your email.")]
    [InlineData(true, "Thank you for confirming your email.")]
    public async Task ValidEmailConfirmationPassesDecodedTokenAndDisplaysIdentityResultAsync(bool succeeds, string message)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByIdAsync("member").Returns(user);
        account.Users.ConfirmEmailAsync(user, "token").Returns(succeeds ? IdentityResult.Success : IdentityResult.Failed());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ConfirmEmail?userId=member&code=dG9rZW4");

        var component = account.Render<ConfirmEmail>(context);

        await component.WaitForAssertionAsync(() => component.Find(succeeds ? ".notice[data-kind='success']" : ".notice[data-kind='error']").TextContent.ShouldBe(message));
        await account.Users.Received(1).ConfirmEmailAsync(user, "token");
    }

    [Fact]
    public async Task SuccessfulEmailChangePassesDecodedTokenUpdatesUsernameAndRefreshesSignInAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByIdAsync("member").Returns(user);
        account.Users.ChangeEmailAsync(user, "new@example.test", "token").Returns(IdentityResult.Success);
        account.Users.SetUserNameAsync(user, "new@example.test").Returns(IdentityResult.Success);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ConfirmEmailChange?userId=member&email=new%40example.test&code=dG9rZW4");

        var component = account.Render<ConfirmEmailChange>(context);

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='success']").TextContent.ShouldBe("Thank you for confirming your email change."));
        await account.Users.Received(1).ChangeEmailAsync(user, "new@example.test", "token");
        await account.Users.Received(1).SetUserNameAsync(user, "new@example.test");
        await account.SignIn.Received(1).RefreshSignInAsync(user);
    }

    [Fact]
    public async Task MalformedEmailConfirmationShowsExistingFailureWithoutConfirmingAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.FindByIdAsync("member").Returns(new ApplicationUser());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ConfirmEmail?userId=member&code=invalid!");

        var component = account.Render<ConfirmEmail>(context);

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error confirming your email."));
        await account.Users.DidNotReceiveWithAnyArgs().ConfirmEmailAsync(default!, default!);
    }

    [Fact]
    public async Task MalformedEmailChangeRedirectsWithoutChangingEmailOrUsernameAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.FindByIdAsync("member").Returns(new ApplicationUser());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ConfirmEmailChange?userId=member&email=new%40example.test&code=invalid!");

        account.Render<ConfirmEmailChange>(context);

        navigation.Uri.ShouldBe("http://localhost/Account/Login");
        account.StatusCookie.ShouldContain("Invalid email change confirmation link");
        await account.Users.DidNotReceiveWithAnyArgs().ChangeEmailAsync(default!, default!, default!);
        await account.Users.DidNotReceiveWithAnyArgs().SetUserNameAsync(default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task UsernameFailureShowsPartialEmailChangeAndDoesNotRefreshSignInAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByIdAsync("member").Returns(user);
        account.Users.ChangeEmailAsync(user, "new@example.test", "token").Returns(IdentityResult.Success);
        account.Users.SetUserNameAsync(user, "new@example.test").Returns(IdentityResult.Failed());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ConfirmEmailChange?userId=member&email=new%40example.test&code=dG9rZW4");

        var component = account.Render<ConfirmEmailChange>(context);

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent
            .ShouldBe("Error: Your email was changed, but your user name could not be updated."));
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task MissingEmailChangeUserDisplaysActualRequestedIdAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ConfirmEmailChange?userId=missing-id&email=new%40example.test&code=dG9rZW4");

        var component = account.Render<ConfirmEmailChange>(context);

        component.Markup.ShouldContain("missing-id");
        component.Markup.ShouldNotContain("{userId}");
        await account.Users.DidNotReceiveWithAnyArgs().ChangeEmailAsync(default!, default!, default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?code=invalid!")]
    public async Task InvalidResetLinkCannotResetPasswordAfterInitializationRedirectAsync(string query)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ResetPassword" + query);
        var component = account.Render<ResetPassword>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        component.Instance.SetInputValue("Code", "posted-token");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidPasswordReset");
        component.FindAll(".validation-message").ShouldBeEmpty();
        await account.Users.DidNotReceiveWithAnyArgs().FindByEmailAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default!);
    }
}
