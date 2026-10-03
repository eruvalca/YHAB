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
using YHAB.Features.Account.Services;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class RegisterConfirmationTests
{
    [Fact]
    public async Task MissingEmailRedirectsHomeWithoutLookingUpUserAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/RegisterConfirmation");

        account.Render<RegisterConfirmation>(context);

        navigation.Uri.ShouldBe("http://localhost/");
        await account.Users.DidNotReceiveWithAnyArgs().FindByEmailAsync(default!);
    }

    [Fact]
    public async Task UnknownEmailReturnsNotFoundWithoutEchoingAddressOrGeneratingTokenAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/RegisterConfirmation?email=private%40example.test");

        var component = account.Render<RegisterConfirmation>(context);

        account.Http.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error finding user for unspecified email");
        component.Markup.ShouldNotContain("private@example.test");
        component.FindAll("a").ShouldBeEmpty();
        await account.Users.Received(1).FindByEmailAsync("private@example.test");
        await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
    }

    [Fact]
    public async Task RealEmailSenderShowsInstructionsWithoutExposingConfirmationTokenAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.FindByEmailAsync("member@example.test").Returns(new ApplicationUser());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/RegisterConfirmation?email=member%40example.test");

        var component = account.Render<RegisterConfirmation>(context);

        component.Find("p[role='alert']").TextContent.ShouldBe("Please check your email to confirm your account.");
        component.FindAll("a").ShouldBeEmpty();
        await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
        await account.Emails.DidNotReceiveWithAnyArgs().SendConfirmationLinkAsync(default!, default!, default!);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("&returnUrl=%2Fevents%3Fpage%3D2", "&returnUrl=%2Fevents%3Fpage%3D2")]
    public async Task DevelopmentEmailSenderRendersEncodedConfirmationLinkForExactUserAsync(string returnQuery, string expectedReturnQuery)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        context.Services.AddSingleton<IEmailSender<ApplicationUser>>(new IdentityNoOpEmailSender());
        var user = new ApplicationUser();
        account.Users.FindByEmailAsync("member@example.test").Returns(user);
        account.Users.GetUserIdAsync(user).Returns("member/id");
        account.Users.GenerateEmailConfirmationTokenAsync(user).Returns("token+/=");
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("Account/RegisterConfirmation?email=member%40example.test" + returnQuery);

        var component = account.Render<RegisterConfirmation>(context);

        component.Find("a[href^='http://localhost/Account/ConfirmEmail']").GetAttribute("href")
            .ShouldBe("http://localhost/Account/ConfirmEmail?userId=member%2Fid&code=dG9rZW4rLz0" + expectedReturnQuery);
        component.FindAll("p[role='alert']").ShouldBeEmpty();
        await account.Users.Received(1).GenerateEmailConfirmationTokenAsync(user);
    }
}
