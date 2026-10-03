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
public sealed class DeletePersonalDataTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SuccessfulDeletionChecksPasswordOnlyWhenRequiredThenSignsOutAsync(bool hasPassword)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<DeletePersonalData>();
        account.Users.HasPasswordAsync(user).Returns(hasPassword);
        account.Users.CheckPasswordAsync(user, "current-password").Returns(true);
        account.Users.DeleteAsync(user).Returns(IdentityResult.Success);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/DeletePersonalData?confirm=true");
        var component = account.Render<DeletePersonalData>(context);
        if (hasPassword)
        {
            await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "current-password" });
        }
        else
        {
            component.FindAll("input[type='password']").ShouldBeEmpty();
        }

        await component.Find("form").SubmitAsync();

        if (hasPassword)
        {
            await account.Users.Received(1).CheckPasswordAsync(user, "current-password");
        }
        else
        {
            await account.Users.DidNotReceiveWithAnyArgs().CheckPasswordAsync(default!, default!);
        }
        await account.Users.Received(1).DeleteAsync(user);
        await account.SignIn.Received(1).SignOutAsync();
        Received.InOrder(() =>
        {
            _ = account.Users.DeleteAsync(user);
            _ = account.SignIn.SignOutAsync();
        });
        logger.GetLoggedEventIds().ShouldContain(1013);
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/DeletePersonalData");
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong-password")]
    public async Task IncorrectOrEmptyRequiredPasswordPreventsDeletionAndSignOutAsync(string password)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<DeletePersonalData>();
        account.Users.HasPasswordAsync(user).Returns(true);
        account.Users.CheckPasswordAsync(user, password).Returns(false);
        var component = account.Render<DeletePersonalData>(context);
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = password });

        await component.Find("form").SubmitAsync();

        component.Find(".notice[data-kind='error']").TextContent.ShouldContain("Incorrect password.");
        await account.Users.Received(1).CheckPasswordAsync(user, password);
        await account.Users.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
        await account.SignIn.DidNotReceive().SignOutAsync();
        logger.GetLoggedEventIds().ShouldNotContain(1013);
    }

    [Fact]
    public async Task FailedDeletionThrowsWithoutSigningOutOrLoggingSuccessAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<DeletePersonalData>();
        account.Users.DeleteAsync(user).Returns(IdentityResult.Failed());
        var component = account.Render<DeletePersonalData>(context);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => component.Find("form").SubmitAsync());

        exception.Message.ShouldBe("Unexpected error occurred deleting user.");
        await account.Users.Received(1).DeleteAsync(user);
        await account.SignIn.DidNotReceive().SignOutAsync();
        logger.GetLoggedEventIds().ShouldNotContain(1013);
    }

    [Fact]
    public async Task MissingUserIsRejectedOnInitializationAndSubmissionWithoutDeletingAnythingAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var component = account.Render<DeletePersonalData>(context);
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        navigation.NavigateTo("Account/Manage/DeletePersonalData");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        account.StatusCookie.ShouldContain("Unable to load user");
        await account.Users.DidNotReceiveWithAnyArgs().HasPasswordAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().CheckPasswordAsync(default!, default!);
        await account.Users.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
        await account.SignIn.DidNotReceive().SignOutAsync();
    }
}
