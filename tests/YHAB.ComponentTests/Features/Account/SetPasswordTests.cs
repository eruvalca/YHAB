using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class SetPasswordTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(100)]
    public async Task ValidPasswordAtEitherLengthBoundaryAddsPasswordAndRefreshesSessionAsync(int length)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var password = new string('N', length);
        account.Users.AddPasswordAsync(user, password).Returns(IdentityResult.Success);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/SetPassword?from=profile");
        var component = account.Render<SetPassword>(context);
        await FillPasswordAsync(component, password, password);

        await component.Find("form").SubmitAsync();

        await account.Users.Received(1).AddPasswordAsync(user, password);
        await account.SignIn.Received(1).RefreshSignInAsync(user);
        Received.InOrder(() =>
        {
            _ = account.Users.AddPasswordAsync(user, password);
            _ = account.SignIn.RefreshSignInAsync(user);
        });
        account.StatusCookie.ShouldContain("Your password has been set.");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/SetPassword");
    }

    [Fact]
    public async Task RejectedPasswordShowsIdentityErrorsWithoutRefreshingSessionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.AddPasswordAsync(user, "new-password").Returns(IdentityResult.Failed(
            new IdentityError { Description = "Password requires a digit" },
            new IdentityError { Description = "Password requires a symbol" }));
        var component = account.Render<SetPassword>(context);
        await FillPasswordAsync(component, "new-password", "new-password");

        await component.Find("form").SubmitAsync();

        var message = component.Find(".notice[data-kind='error']").TextContent;
        message.ShouldContain("Password requires a digit");
        message.ShouldContain("Password requires a symbol");
        await account.Users.Received(1).AddPasswordAsync(user, "new-password");
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        account.StatusCookie.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, false, "New password field is required")]
    [InlineData(5, false, "at least 6 and at max 100")]
    [InlineData(101, false, "at least 6 and at max 100")]
    [InlineData(8, true, "do not match")]
    public async Task InvalidPasswordFormDoesNotAddPasswordAsync(int length, bool mismatch, string expectedError)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Authenticate();
        var component = account.Render<SetPassword>(context);
        var password = new string('N', length);
        await FillPasswordAsync(component, password, mismatch ? "different-password" : password);

        await component.Find("form").SubmitAsync();

        component.Find(".error-text[role='alert']").TextContent.ShouldContain(expectedError);
        await account.Users.DidNotReceiveWithAnyArgs().AddPasswordAsync(default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task AccountWithPasswordIsDirectedToChangePasswordBeforeAnyMutationAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.HasPasswordAsync(user).Returns(true);

        account.Render<SetPassword>(context);

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/ChangePassword");
        await account.Users.Received(1).HasPasswordAsync(user);
        await account.Users.DidNotReceiveWithAnyArgs().AddPasswordAsync(default!, default!);
    }

    [Fact]
    public async Task MissingUserIsRejectedOnInitializationAndValidSubmissionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var component = account.Render<SetPassword>(context);
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        navigation.NavigateTo("Account/Manage/SetPassword");
        await FillPasswordAsync(component, "new-password", "new-password");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        account.StatusCookie.ShouldContain("Unable to load user");
        await account.Users.DidNotReceiveWithAnyArgs().HasPasswordAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().AddPasswordAsync(default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    private static async Task FillPasswordAsync(IRenderedComponent<SetPassword> component, string password, string confirmation)
    {
        await component.Find("input[name='Input.NewPassword']").ChangeAsync(new ChangeEventArgs { Value = password });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = confirmation });
    }
}
