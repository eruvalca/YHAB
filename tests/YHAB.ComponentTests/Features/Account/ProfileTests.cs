using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using Profile = YHAB.Features.Account.Pages.Manage.Index;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ProfileTests
{
    [Fact]
    public async Task ProfileLoadsCurrentDisplayNameAndSavingUnchangedNameAvoidsWriteAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        user.DisplayName = "Alice";
        var component = account.Render<Profile>(context);
        component.Find("input[name='Input.DisplayName']").GetAttribute("value").ShouldBe("Alice");
        component.FindAll("#username").ShouldBeEmpty();
        component.FindAll("input[name='Input.PhoneNumber']").ShouldBeEmpty();
        component.Find("#display-name-hint").TextContent.ShouldContain("email address stays private");

        await component.Find("form").SubmitAsync();

        await account.Users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await account.SignIn.Received(1).RefreshSignInAsync(user);
        account.StatusCookie.ShouldContain("Your profile has been updated");
    }

    [Fact]
    public async Task ProfileShowsTheFriendlyFallbackWhenNoDisplayNameIsSetAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        user.DisplayName = null;
        var component = account.Render<Profile>(context);
        component.Find("input[name='Input.DisplayName']").GetAttribute("value").ShouldBe("Member");
        component.Find("#display-name-hint").TextContent.ShouldContain("Leave it blank to use");

        await component.Find("form").SubmitAsync();

        await account.Users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await account.SignIn.Received(1).RefreshSignInAsync(user);
    }

    [Fact]
    public async Task ChangedDisplayNameIsTrimmedSavedAndRefreshedBeforeTheStatusMessageAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        user.DisplayName = "Old name";
        account.Users.UpdateAsync(user).Returns(IdentityResult.Success);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage?from=profile");
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.DisplayName']").ChangeAsync(new ChangeEventArgs { Value = "  Alice  " });

        await component.Find("form").SubmitAsync();

        component.FindAll(".error-text[role='alert']").ShouldBeEmpty();
        user.DisplayName.ShouldBe("Alice");
        await account.Users.Received(1).UpdateAsync(user);
        await account.SignIn.Received(1).RefreshSignInAsync(user);
        Received.InOrder(() =>
        {
            _ = account.Users.UpdateAsync(user);
            _ = account.SignIn.RefreshSignInAsync(user);
        });
        account.StatusCookie.ShouldContain("Your profile has been updated");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage");
    }

    [Fact]
    public async Task ClearingTheDisplayNameRestoresTheFriendlyFallbackAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        user.DisplayName = "Alice";
        account.Users.UpdateAsync(user).Returns(IdentityResult.Success);
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.DisplayName']").ChangeAsync(new ChangeEventArgs { Value = "" });

        await component.Find("form").SubmitAsync();

        user.DisplayName.ShouldBeNull();
        await account.Users.Received(1).UpdateAsync(user);
        account.StatusCookie.ShouldContain("Your profile has been updated");
    }

    [Fact]
    public async Task DisplayNameMatchingTheEmailIsRejectedWithoutWritingOrRefreshingSessionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        user.DisplayName = "Alice";
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.DisplayName']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });

        await component.Find("form").SubmitAsync();

        user.DisplayName.ShouldBe("Alice");
        await account.Users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        account.StatusCookie.ShouldContain("Choose a display name that does not include your email address");
        account.StatusCookie.ShouldNotContain("Your profile has been updated");
    }

    [Theory]
    [InlineData("Alice (member@example.test)")]
    [InlineData("Alice (MEMBER@EXAMPLE.TEST)")]
    public async Task DisplayNameContainingTheEmailIsRejectedWithoutWritingOrRefreshingSessionAsync(string displayName)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        user.DisplayName = "Alice";
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.DisplayName']").ChangeAsync(new ChangeEventArgs { Value = displayName });

        await component.Find("form").SubmitAsync();

        user.DisplayName.ShouldBe("Alice");
        await account.Users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        account.StatusCookie.ShouldContain("Choose a display name that does not include your email address");
        account.StatusCookie.ShouldNotContain("Your profile has been updated");
    }

    [Fact]
    public async Task DisplayNameUpdateFailureReportsFailureAndDoesNotRefreshSessionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        user.DisplayName = "Old name";
        account.Users.UpdateAsync(user).Returns(IdentityResult.Failed());
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.DisplayName']").ChangeAsync(new ChangeEventArgs { Value = "Alice" });

        await component.Find("form").SubmitAsync();

        await account.Users.Received(1).UpdateAsync(user);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        account.StatusCookie.ShouldContain("Error: Failed to update your display name.");
        account.StatusCookie.ShouldNotContain("Your profile has been updated");
    }

    [Fact]
    public async Task DisplayNameOverTheMaximumLengthDoesNotWriteOrRefreshSessionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Authenticate();
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.DisplayName']").ChangeAsync(new ChangeEventArgs { Value = new string('a', 81) });

        await component.Find("form").SubmitAsync();

        component.Find(".error-text[role='alert']").TextContent.ShouldContain("80");
        await account.Users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        account.StatusCookie.ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingUserIsRejectedOnInitializationAndSubmissionWithoutReadingOrWritingProfileAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var component = account.Render<Profile>(context);
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        navigation.NavigateTo("Account/Manage");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        account.StatusCookie.ShouldContain("Unable to load user");
        await account.Users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }
}
