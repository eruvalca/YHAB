using System.Diagnostics.CodeAnalysis;
using Bogus;
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
    public async Task ProfileLoadsCurrentValuesAndSavingUnchangedPhoneAvoidsWriteAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var fake = new Faker { Random = new Randomizer(724) };
        var username = fake.Internet.UserName();
        var phone = fake.Phone.PhoneNumber("###-###-####");
        account.Users.GetUserNameAsync(user).Returns(username);
        account.Users.GetPhoneNumberAsync(user).Returns(phone);
        var component = account.Render<Profile>(context);
        component.Find("#username").GetAttribute("value").ShouldBe(username);
        component.Find("#username").HasAttribute("disabled").ShouldBeTrue();
        component.Find("input[name='Input.PhoneNumber']").GetAttribute("value").ShouldBe(phone);

        await component.Find("form").SubmitAsync();

        await account.Users.DidNotReceiveWithAnyArgs().SetPhoneNumberAsync(default!, default);
        await account.SignIn.Received(1).RefreshSignInAsync(user);
        account.StatusCookie.ShouldContain("Your profile has been updated");
    }

    [Theory]
    [InlineData("+1 (312) 555-0123")]
    [InlineData(null)]
    public async Task ChangedOrRemovedPhoneIsSavedBeforeRefreshingSessionAsync(string? phone)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetPhoneNumberAsync(user).Returns("+1 (312) 555-0199");
        account.Users.SetPhoneNumberAsync(user, phone).Returns(IdentityResult.Success);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage?from=profile");
        var component = account.Render<Profile>(context);
        if (phone is null)
        {
            // Supply the nullable posted value because bUnit does not run static SSR's form mapper.
            component.Instance.SetInputValue("PhoneNumber", null);
        }
        else
        {
            await component.Find("input[name='Input.PhoneNumber']").ChangeAsync(new ChangeEventArgs { Value = phone });
        }

        await component.Find("form").SubmitAsync();

        component.FindAll(".error-text[role='alert']").ShouldBeEmpty();
        await account.Users.Received(1).SetPhoneNumberAsync(user, phone);
        await account.SignIn.Received(1).RefreshSignInAsync(user);
        Received.InOrder(() =>
        {
            _ = account.Users.SetPhoneNumberAsync(user, phone);
            _ = account.SignIn.RefreshSignInAsync(user);
        });
        account.StatusCookie.ShouldContain("Your profile has been updated");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage");
    }

    [Fact]
    public async Task PhoneUpdateFailureReportsFailureAndDoesNotRefreshSessionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.SetPhoneNumberAsync(user, "312-555-0123").Returns(IdentityResult.Failed());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage?from=profile");
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.PhoneNumber']").ChangeAsync(new ChangeEventArgs { Value = "312-555-0123" });

        await component.Find("form").SubmitAsync();

        await account.Users.Received(1).SetPhoneNumberAsync(user, "312-555-0123");
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        account.StatusCookie.ShouldContain("Error: Failed to set phone number.");
        account.StatusCookie.ShouldNotContain("Your profile has been updated");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a phone number")]
    public async Task InvalidPhoneNumberDoesNotWriteOrRefreshSessionAsync(string phone)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Authenticate();
        var component = account.Render<Profile>(context);
        await component.Find("input[name='Input.PhoneNumber']").ChangeAsync(new ChangeEventArgs { Value = phone });

        await component.Find("form").SubmitAsync();

        component.Find(".error-text[role='alert']").TextContent.ShouldContain("not a valid phone number");
        await account.Users.DidNotReceiveWithAnyArgs().SetPhoneNumberAsync(default!, default);
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
        await account.Users.DidNotReceiveWithAnyArgs().GetUserNameAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().GetPhoneNumberAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().SetPhoneNumberAsync(default!, default);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }
}
