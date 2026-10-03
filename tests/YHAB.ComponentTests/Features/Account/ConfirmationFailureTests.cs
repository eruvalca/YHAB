using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
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
public sealed class ConfirmationFailureTests
{
    [Theory]
    [InlineData("?code=dG9rZW4")]
    [InlineData("?userId=member")]
    public async Task MissingEmailConfirmationParameterRedirectsHomeBeforeUserLookupAsync(string query)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ConfirmEmail" + query);

        account.Render<ConfirmEmail>(context);

        navigation.Uri.ShouldBe("http://localhost/");
        await account.Users.DidNotReceiveWithAnyArgs().FindByIdAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().ConfirmEmailAsync(default!, default!);
    }

    [Fact]
    public async Task MissingConfirmationUserReturnsNotFoundAndDoesNotConfirmAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ConfirmEmail?userId=missing-user&code=dG9rZW4");

        var component = account.Render<ConfirmEmail>(context);

        account.Http.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error loading user with ID missing-user");
        await account.Users.Received(1).FindByIdAsync("missing-user");
        await account.Users.DidNotReceiveWithAnyArgs().ConfirmEmailAsync(default!, default!);
    }

    [Theory]
    [InlineData("?email=new%40example.test&code=dG9rZW4")]
    [InlineData("?userId=member&code=dG9rZW4")]
    [InlineData("?userId=member&email=new%40example.test")]
    public async Task MissingEmailChangeParameterRedirectsWithInvalidLinkStatusBeforeLookupAsync(string query)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ConfirmEmailChange" + query);

        account.Render<ConfirmEmailChange>(context);

        navigation.Uri.ShouldBe("http://localhost/Account/Login");
        account.StatusCookie.ShouldContain("Error: Invalid email change confirmation link.");
        await account.Users.DidNotReceiveWithAnyArgs().FindByIdAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().ChangeEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task RejectedEmailChangeShowsFailureWithoutChangingUsernameOrRefreshingSignInAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByIdAsync("member").Returns(user);
        account.Users.ChangeEmailAsync(user, "new@example.test", "token").Returns(IdentityResult.Failed());
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("Account/ConfirmEmailChange?userId=member&email=new%40example.test&code=dG9rZW4");

        var component = account.Render<ConfirmEmailChange>(context);

        component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error changing email.");
        await account.Users.Received(1).ChangeEmailAsync(user, "new@example.test", "token");
        await account.Users.DidNotReceiveWithAnyArgs().SetUserNameAsync(default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task FailedPasswordResetDisplaysAllIdentityErrorsAndPreservesResetPageAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = new ApplicationUser();
        account.Users.FindByEmailAsync("member@example.test").Returns(user);
        account.Users.ResetPasswordAsync(user, "token", "password").Returns(IdentityResult.Failed(
            new IdentityError { Description = "Token expired" }, new IdentityError { Description = "Password rejected" }));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/ResetPassword?code=dG9rZW4");
        var component = account.Render<ResetPassword>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = "password" });

        await component.Find("form").SubmitAsync();

        component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error: Token expired, Password rejected");
        navigation.Uri.ShouldBe("http://localhost/Account/ResetPassword?code=dG9rZW4");
        await account.Users.Received(1).ResetPasswordAsync(user, "token", "password");
    }

    [Theory]
    [InlineData("not-an-email", "password", "password", "Email")]
    [InlineData("member@example.test", "password", "different", "do not match")]
    [InlineData("member@example.test", "short", "short", "at least 6")]
    public async Task InvalidResetFieldsPreventUserLookupAndPasswordMutationAsync(string email, string password, string confirmation, string expectedError)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/ResetPassword?code=dG9rZW4");
        var component = account.Render<ResetPassword>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = email });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = password });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = confirmation });

        await component.Find("form").SubmitAsync();

        component.Find(".validation-message").TextContent.ShouldContain(expectedError);
        await account.Users.DidNotReceiveWithAnyArgs().FindByEmailAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default!);
    }
}
