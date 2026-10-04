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
using YHAB.Features.Account.Services;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class RegisterConfirmationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyConfirmationPageNeverLooksUpUsersOrExposesTokensAsync(bool requireConfirmation)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Users.Options.SignIn.RequireConfirmedAccount = requireConfirmation;
        context.Services.AddSingleton<IEmailSender<ApplicationUser>>(new IdentityNoOpEmailSender());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/RegisterConfirmation?email=private%40example.test");

        var component = account.Render<RegisterConfirmation>(context);

        component.Markup.ShouldContain(requireConfirmation ? "Please check your email" : "Email confirmation is not required");
        component.Markup.ShouldNotContain("private@example.test");
        component.FindAll("a[href*='ConfirmEmail']").ShouldBeEmpty();
        await account.Users.DidNotReceiveWithAnyArgs().FindByEmailAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
    }
}
