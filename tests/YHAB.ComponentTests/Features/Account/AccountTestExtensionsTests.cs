using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Pages;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountTestExtensionsTests
{
    [Fact]
    public async Task IndependentAccountContextsPreserveRegisteredInstancesAndIsolateStateAsync()
    {
        await using var first = new BunitContext();
        await using var second = new BunitContext();

        var firstAccount = first.ConfigureAccount();
        var secondAccount = second.ConfigureAccount();

        first.Services.GetRequiredService<UserManager<ApplicationUser>>().ShouldBeSameAs(firstAccount.Users);
        first.Services.GetRequiredService<SignInManager<ApplicationUser>>().ShouldBeSameAs(firstAccount.SignIn);
        first.Services.GetRequiredService<IEmailSender<ApplicationUser>>().ShouldBeSameAs(firstAccount.Emails);
        firstAccount.SignIn.UserManager.ShouldBeSameAs(firstAccount.Users);
        firstAccount.Http.RequestServices.ShouldBeSameAs(first.Services);
        second.Services.GetRequiredService<UserManager<ApplicationUser>>().ShouldBeSameAs(secondAccount.Users);
        second.Services.GetRequiredService<SignInManager<ApplicationUser>>().ShouldBeSameAs(secondAccount.SignIn);
        second.Services.GetRequiredService<IEmailSender<ApplicationUser>>().ShouldBeSameAs(secondAccount.Emails);
        secondAccount.Http.RequestServices.ShouldBeSameAs(second.Services);
        secondAccount.Http.ShouldNotBeSameAs(firstAccount.Http);
        secondAccount.Users.ShouldNotBeSameAs(firstAccount.Users);
        secondAccount.SignIn.ShouldNotBeSameAs(firstAccount.SignIn);
        secondAccount.Emails.ShouldNotBeSameAs(firstAccount.Emails);

        firstAccount.Http.Request.Method = HttpMethods.Get;
        firstAccount.Http.Response.Cookies.Append("test-status", "first only");
        var user = firstAccount.Authenticate();

        (await firstAccount.Users.GetUserAsync(firstAccount.Http.User)).ShouldBeSameAs(user);
        (await secondAccount.Users.GetUserAsync(secondAccount.Http.User)).ShouldBeNull();
        secondAccount.Http.Request.Method.ShouldBe(HttpMethods.Post);
        secondAccount.StatusCookie.ShouldBeEmpty();
        firstAccount.StatusCookie.ShouldContain("first only");
    }

    [Fact]
    public async Task CapturedLogsPreserveRegistrationSnapshotTimingAndOrderedDuplicateEventsAsync()
    {
        await using var context = new BunitContext();
        context.ConfigureAccount();
        var logger = context.CaptureLogs<Login>();
        var otherLogger = context.CaptureLogs<Register>();
        context.Services.GetRequiredService<ILogger<Login>>().ShouldBeSameAs(logger);
        logger.IsEnabled(LogLevel.Information).ShouldBeTrue();
        var firstEvent = LoggerMessage.Define(LogLevel.Information, new EventId(17), "First test event");
        var secondEvent = LoggerMessage.Define(LogLevel.Warning, new EventId(23), "Second test event");

        logger.GetLoggedEventIds().ShouldBeEmpty();
        firstEvent(logger, null);
        var firstSnapshot = logger.GetLoggedEventIds();
        secondEvent(logger, null);
        firstEvent(logger, null);
        secondEvent(otherLogger, null);

        firstSnapshot.ShouldBe([17]);
        logger.GetLoggedEventIds().ShouldBe([17, 23, 17]);
        otherLogger.GetLoggedEventIds().ShouldBe([23]);
    }

    [Fact]
    public async Task FormAndInputExtensionsPopulateRealComponentSubmissionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.SignIn.PasskeySignInAsync("helper-credential").Returns(SignInResult.Success);
        var component = account.Render<Login>(context);

        component.Instance.SetFormValue("ReturnUrl", "/helper-return");
        component.Instance.SetInputValue("Passkey", new PasskeyInputModel { CredentialJson = "helper-credential" });
        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() =>
            context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/helper-return"));
        await account.SignIn.Received(1).PasskeySignInAsync("helper-credential");
        await account.SignIn.DidNotReceiveWithAnyArgs().PasswordSignInAsync(default(string)!, default!, default, default);
        component.FindAll(".validation-message").ShouldBeEmpty();
    }
}
