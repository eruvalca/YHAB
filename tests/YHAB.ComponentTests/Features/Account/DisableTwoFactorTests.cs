using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class DisableTwoFactorTests
{
    [Theory]
    [InlineData(false, false, "Two-factor authentication is already disabled.")]
    [InlineData(true, false, "Two-factor authentication could not be disabled.")]
    [InlineData(true, true, "2fa has been disabled.")]
    public async Task DisableReportsOutcomeAndLogsOnlyActualSuccessfulMutationAsync(bool enabled, bool succeeds, string message)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var logger = context.CaptureLogs<Disable2fa>();
        account.Users.GetTwoFactorEnabledAsync(user).Returns(enabled);
        account.Users.SetTwoFactorEnabledAsync(user, false).Returns(succeeds ? IdentityResult.Success : IdentityResult.Failed());
        var component = account.Render<Disable2fa>(context);

        await component.Find("form").SubmitAsync();

        account.StatusCookie.ShouldContain(message);
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe(enabled && !succeeds
            ? "http://localhost/Account/Manage/Disable2fa"
            : "http://localhost/Account/Manage/TwoFactorAuthentication");
        if (enabled && succeeds)
        {
            logger.GetLoggedEventIds().ShouldContain(1014);
        }
        else
        {
            logger.GetLoggedEventIds().ShouldNotContain(1014);
        }
        if (!enabled)
        {
            await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
        }
    }

    [Fact]
    public async Task OpeningDisablePageWhenAlreadyDisabledRedirectsWithStatusAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Authenticate();
        account.Http.Request.Method = HttpMethods.Get;

        account.Render<Disable2fa>(context);

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/TwoFactorAuthentication");
        account.StatusCookie.ShouldContain("already disabled");
        await account.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
    }
}
