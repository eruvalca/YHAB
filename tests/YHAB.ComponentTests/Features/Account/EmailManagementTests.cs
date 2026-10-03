using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Bogus;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class EmailManagementTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentEmailAndVerificationStateAreLoadedWithoutSendingEmailAsync(bool confirmed)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var fake = new Faker { Random = new Randomizer(216) };
        var email = fake.Internet.Email();
        account.Users.GetEmailAsync(user).Returns(email);
        account.Users.IsEmailConfirmedAsync(user).Returns(confirmed);

        var component = account.Render<Email>(context);

        component.Find("#email").GetAttribute("value").ShouldBe(email);
        component.Find("input[name='Input.NewEmail']").GetAttribute("value").ShouldBe(email);
        component.FindAll("button[form='send-verification-form']").Count.ShouldBe(confirmed ? 0 : 1);
        if (confirmed)
        {
            component.Markup.ShouldContain("Email confirmed");
        }
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
    }

    [Fact]
    public async Task UnchangedEmailReportsNoChangeWithoutIssuingTokenOrSendingEmailAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetEmailAsync(user).Returns("current@example.test");
        var component = account.Render<Email>(context);

        await component.Find("form.account-form").SubmitAsync();

        component.Find(".notice").TextContent.ShouldContain("Your email is unchanged.");
        await account.Users.DidNotReceiveWithAnyArgs().GenerateChangeEmailTokenAsync(default!, default!);
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
        await account.Users.DidNotReceiveWithAnyArgs().SetEmailAsync(default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedEmailIncludingCaseChangeReceivesEscapedConfirmationLinkWithoutChangingAccountYetAsync(bool changesOnlyCase)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var fake = new Faker { Random = new Randomizer(935) };
        var newEmail = changesOnlyCase ? "CURRENT@example.test" : $"{fake.Internet.UserName()}+updates@example.test";
        const string UserId = "member/42&tab=profile";
        const string Token = "identity/+ token?&";
        account.Users.GetEmailAsync(user).Returns("current@example.test");
        account.Users.GetUserIdAsync(user).Returns(UserId);
        account.Users.GenerateChangeEmailTokenAsync(user, newEmail).Returns(Token);
        string? callback = null;
        account.Emails.SendConfirmationLinkAsync(user, newEmail, Arg.Do<string>(link => callback = link)).Returns(Task.CompletedTask);
        var component = account.Render<Email>(context);
        await component.Find("input[name='Input.NewEmail']").ChangeAsync(new ChangeEventArgs { Value = newEmail });

        await component.Find("form.account-form").SubmitAsync();

        await account.Users.Received(1).GenerateChangeEmailTokenAsync(user, newEmail);
        await account.Emails.Received(1).SendConfirmationLinkAsync(user, newEmail, Arg.Any<string>());
        AssertConfirmationLink(callback, "ConfirmEmailChange", UserId, Token, newEmail);
        component.Find(".notice").TextContent.ShouldContain("Confirmation link to change email sent");
        await account.Users.DidNotReceiveWithAnyArgs().SetEmailAsync(default!, default);
        await account.Users.DidNotReceiveWithAnyArgs().ChangeEmailAsync(default!, default!, default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Theory]
    [InlineData("", "New email field is required")]
    [InlineData("invalid-address", "not a valid e-mail address")]
    public async Task InvalidNewEmailDoesNotGenerateTokenOrSendConfirmationAsync(string email, string expectedError)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetEmailAsync(user).Returns("current@example.test");
        var component = account.Render<Email>(context);
        await component.Find("input[name='Input.NewEmail']").ChangeAsync(new ChangeEventArgs { Value = email });

        await component.Find("form.account-form").SubmitAsync();

        component.Find(".error-text[role='alert']").TextContent.ShouldContain(expectedError);
        await account.Users.DidNotReceiveWithAnyArgs().GenerateChangeEmailTokenAsync(default!, default!);
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
    }

    [Fact]
    public async Task VerificationUsesCurrentEmailEvenWhenNewEmailInputHasChangedAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        const string UserId = "member/42&tab=profile";
        const string Token = "confirm/+ token?&";
        account.Users.GetEmailAsync(user).Returns("current@example.test");
        account.Users.GetUserIdAsync(user).Returns(UserId);
        account.Users.GenerateEmailConfirmationTokenAsync(user).Returns(Token);
        string? callback = null;
        account.Emails.SendConfirmationLinkAsync(user, "current@example.test", Arg.Do<string>(link => callback = link)).Returns(Task.CompletedTask);
        var component = account.Render<Email>(context);
        await component.Find("input[name='Input.NewEmail']").ChangeAsync(new ChangeEventArgs { Value = "new@example.test" });

        await component.Find("#send-verification-form").SubmitAsync();

        await account.Users.Received(1).GenerateEmailConfirmationTokenAsync(user);
        await account.Emails.Received(1).SendConfirmationLinkAsync(user, "current@example.test", Arg.Any<string>());
        AssertConfirmationLink(callback, "ConfirmEmail", UserId, Token);
        component.Find(".notice").TextContent.ShouldContain("Verification email sent");
        await account.Users.DidNotReceiveWithAnyArgs().GenerateChangeEmailTokenAsync(default!, default!);
        await account.Users.DidNotReceiveWithAnyArgs().SetEmailAsync(default!, default);
    }

    [Fact]
    public async Task AccountWithoutEmailCannotSendVerificationAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetEmailAsync(user).Returns((string?)null);
        var component = account.Render<Email>(context);

        await component.Find("#send-verification-form").SubmitAsync();

        await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
        component.FindAll(".notice").ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingUserIsRejectedOnInitializationAndChangedEmailSubmissionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var component = account.Render<Email>(context);
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        navigation.NavigateTo("Account/Manage/Email");
        await component.Find("input[name='Input.NewEmail']").ChangeAsync(new ChangeEventArgs { Value = "new@example.test" });

        await component.Find("form.account-form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        account.StatusCookie.ShouldContain("Unable to load user");
        await account.Users.DidNotReceiveWithAnyArgs().GetEmailAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().IsEmailConfirmedAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().GenerateChangeEmailTokenAsync(default!, default!);
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
    }

    private static void AssertConfirmationLink(string? callback, string page, string userId, string token, string? email = null)
    {
        callback.ShouldNotBeNull();
        callback.ShouldContain("&amp;code=");
        var uri = new Uri(WebUtility.HtmlDecode(callback));
        uri.GetLeftPart(UriPartial.Path).ShouldBe($"http://localhost/Account/{page}");
        var query = QueryHelpers.ParseQuery(uri.Query);
        query.Count.ShouldBe(email is null ? 2 : 3);
        query["userId"].ToString().ShouldBe(userId);
        Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["code"].ToString())).ShouldBe(token);
        if (email is not null)
        {
            query["email"].ToString().ShouldBe(email);
        }
    }
}
