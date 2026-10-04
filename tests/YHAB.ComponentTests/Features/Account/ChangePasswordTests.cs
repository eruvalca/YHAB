using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages.Manage;
using YHAB.Features.Account.Services;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ChangePasswordTests
{
    [Fact]
    public async Task CancellationAfterPasswordCommitStillRefreshesSessionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        using var request = new CancellationTokenSource();
        var cancellation = context.Services.GetRequiredService<IdentityCancellation>();
        cancellation.Token = request.Token;
        account.Users.HasPasswordAsync(user).Returns(true);
        account.Users.ChangePasswordAsync(user, "current-password", "new-password").Returns(async _ =>
        {
            await request.CancelAsync();
            return IdentityResult.Success;
        });
        account.SignIn.RefreshSignInAsync(user).Returns(_ =>
        {
            cancellation.Token.CanBeCanceled.ShouldBeFalse();
            return Task.CompletedTask;
        });
        var component = account.Render<ChangePassword>(context);
        await FillPasswordAsync(component, "current-password", "new-password", "new-password");

        await component.Find("form").SubmitAsync();

        await account.SignIn.Received(1).RefreshSignInAsync(user);
        account.StatusCookie.ShouldContain("Your password has been changed");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(100)]
    public async Task ValidPasswordAtEitherLengthBoundaryChangesPasswordAndRefreshesSessionAsync(int length)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<ChangePassword>();
        var password = new string('N', length);
        account.Users.HasPasswordAsync(user).Returns(true);
        account.Users.ChangePasswordAsync(user, "current-password", password).Returns(IdentityResult.Success);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/ChangePassword?from=profile");
        var component = account.Render<ChangePassword>(context);
        await FillPasswordAsync(component, "current-password", password, password);

        await component.Find("form").SubmitAsync();

        await account.Users.Received(1).ChangePasswordAsync(user, "current-password", password);
        await account.SignIn.Received(1).RefreshSignInAsync(user);
        Received.InOrder(() =>
        {
            _ = account.Users.ChangePasswordAsync(user, "current-password", password);
            _ = account.SignIn.RefreshSignInAsync(user);
        });
        account.StatusCookie.ShouldContain("Your password has been changed");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/ChangePassword");
        logger.GetLoggedEventIds().ShouldContain(1012);
    }

    [Fact]
    public async Task RejectedPasswordChangeShowsAllErrorsWithoutRefreshingOrReportingSuccessAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<ChangePassword>();
        account.Users.HasPasswordAsync(user).Returns(true);
        account.Users.ChangePasswordAsync(user, "current-password", "new-password").Returns(IdentityResult.Failed(
            new IdentityError { Description = "Current password is incorrect" },
            new IdentityError { Description = "New password was used previously" }));
        var component = account.Render<ChangePassword>(context);
        await FillPasswordAsync(component, "current-password", "new-password", "new-password");

        await component.Find("form").SubmitAsync();

        var message = component.Find(".notice[data-kind='error']").TextContent;
        message.ShouldContain("Current password is incorrect");
        message.ShouldContain("New password was used previously");
        await account.Users.Received(1).ChangePasswordAsync(user, "current-password", "new-password");
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        account.StatusCookie.ShouldBeEmpty();
        logger.GetLoggedEventIds().ShouldNotContain(1012);
    }

    [Theory]
    [InlineData("", 8, false, "Current password field is required")]
    [InlineData("current-password", 0, false, "New password field is required")]
    [InlineData("current-password", 5, false, "at least 6 and at max 100")]
    [InlineData("current-password", 101, false, "at least 6 and at max 100")]
    [InlineData("current-password", 8, true, "do not match")]
    public async Task InvalidPasswordFormDoesNotAttemptChangeAsync(string current, int length, bool mismatch, string expectedError)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.HasPasswordAsync(user).Returns(true);
        var component = account.Render<ChangePassword>(context);
        var password = new string('N', length);
        await FillPasswordAsync(component, current, password, mismatch ? "different-password" : password);

        await component.Find("form").SubmitAsync();

        component.Find(".error-text[role='alert']").TextContent.ShouldContain(expectedError);
        await account.Users.DidNotReceiveWithAnyArgs().ChangePasswordAsync(default!, default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task AccountWithoutPasswordIsDirectedToSetPasswordBeforeAnyMutationAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.HasPasswordAsync(user).Returns(false);

        account.Render<ChangePassword>(context);

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/SetPassword");
        await account.Users.Received(1).HasPasswordAsync(user);
        await account.Users.DidNotReceiveWithAnyArgs().ChangePasswordAsync(default!, default!, default!);
    }

    [Fact]
    public async Task MissingUserIsRejectedOnInitializationAndValidSubmissionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var component = account.Render<ChangePassword>(context);
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        navigation.NavigateTo("Account/Manage/ChangePassword");
        await FillPasswordAsync(component, "current-password", "new-password", "new-password");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        account.StatusCookie.ShouldContain("Unable to load user");
        await account.Users.DidNotReceiveWithAnyArgs().HasPasswordAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().ChangePasswordAsync(default!, default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    private static async Task FillPasswordAsync(IRenderedComponent<ChangePassword> component, string current, string password, string confirmation)
    {
        await component.Find("input[name='Input.OldPassword']").ChangeAsync(new ChangeEventArgs { Value = current });
        await component.Find("input[name='Input.NewPassword']").ChangeAsync(new ChangeEventArgs { Value = password });
        await component.Find("input[name='Input.ConfirmPassword']").ChangeAsync(new ChangeEventArgs { Value = confirmation });
    }
}
