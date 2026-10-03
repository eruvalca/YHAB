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
public sealed class RecoveryCodesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRecoveryCodesShowsVisibleFailureWithoutSuccessLogAsync(bool empty)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<GenerateRecoveryCodes>();
        account.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        account.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns(empty ? [] : (IEnumerable<string>?)null);
        var component = account.Render<GenerateRecoveryCodes>(context);

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent
            .ShouldBe("Error: Recovery codes could not be generated. Please try again."));
        component.FindAll(".recovery-code").ShouldBeEmpty();
        component.FindAll(".notice[data-kind='success']").ShouldBeEmpty();
        logger.GetLoggedEventIds().ShouldNotContain(1016);
    }

    [Fact]
    public async Task GeneratedRecoveryCodesAreDisplayedWithSuccessMessageAndLogAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<GenerateRecoveryCodes>();
        account.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        string[] codes = ["first-code", "second-code"];
        account.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns(codes);
        var component = account.Render<GenerateRecoveryCodes>(context);

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.FindAll(".recovery-code").Select(element => element.TextContent).ShouldBe(codes));
        component.Find(".notice[data-kind='success']").TextContent.ShouldBe("You have generated new recovery codes.");
        logger.GetLoggedEventIds().ShouldContain(1016);
        await account.Users.Received(1).GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledTwoFactorRedirectsAndCannotGenerateEvenAfterPreviouslyEnabledPageAsync(bool initiallyEnabled)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<GenerateRecoveryCodes>();
        account.Users.GetTwoFactorEnabledAsync(user).Returns(initiallyEnabled);
        var component = account.Render<GenerateRecoveryCodes>(context);
        account.Users.GetTwoFactorEnabledAsync(user).Returns(false);

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/TwoFactorAuthentication");
        account.StatusCookie.ShouldContain("Enable two-factor authentication before generating recovery codes");
        await account.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
        logger.GetLoggedEventIds().ShouldNotContain(1016);
    }
}
