using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountTwoFactorManagementTests
{
    private static readonly string[] _resetSteps = ["disable", "reset", "refresh"];
    private static readonly string[] _recoveryCodes = ["first-code", "second-code"];

    [Fact]
    public async Task ResetDisablesThenResetsAndRefreshesSignInAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        var steps = new List<string>();
        identity.Users.SetTwoFactorEnabledAsync(user, false).Returns(_ =>
        {
            steps.Add("disable");
            return IdentityResult.Success;
        });
        identity.Users.ResetAuthenticatorKeyAsync(user).Returns(_ =>
        {
            steps.Add("reset");
            return IdentityResult.Success;
        });
        identity.SignIn.RefreshSignInAsync(user).Returns(_ =>
        {
            steps.Add("refresh");
            return Task.CompletedTask;
        });
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.ResetAsync(user);

        result.Value.ShouldBeOfType<ResetAuthenticatorOutcome.ResetCompleted>();
        steps.ShouldBe(_resetSteps);
    }

    [Fact]
    public async Task ResetDisableFailureStopsBeforeResetAndRefreshAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.SetTwoFactorEnabledAsync(user, false).Returns(IdentityResult.Failed());
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.ResetAsync(user);

        result.Value.ShouldBeOfType<ResetAuthenticatorOutcome.DisableFailed>();
        await identity.Users.DidNotReceiveWithAnyArgs().ResetAuthenticatorKeyAsync(default!);
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task ResetKeyFailureReportsPartialDisableAndSkipsRefreshAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.SetTwoFactorEnabledAsync(user, false).Returns(IdentityResult.Success);
        identity.Users.ResetAuthenticatorKeyAsync(user).Returns(IdentityResult.Failed());
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.ResetAsync(user);

        result.Value.ShouldBeOfType<ResetAuthenticatorOutcome.DisabledButKeyResetFailed>();
        await identity.Users.Received(1).SetTwoFactorEnabledAsync(user, false);
        await identity.Users.DidNotReceive().SetTwoFactorEnabledAsync(user, true);
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task AlreadyDisabledDoesNotWriteAgainAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetTwoFactorEnabledAsync(user).Returns(false);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.DisableAsync(user);

        result.Value.ShouldBeOfType<DisableTwoFactorOutcome.AlreadyDisabled>();
        await identity.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
    }

    [Fact]
    public async Task DisableSuccessReturnsDisabledWithoutRefreshingCookieAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        identity.Users.SetTwoFactorEnabledAsync(user, false).Returns(IdentityResult.Success);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.DisableAsync(user);

        result.Value.ShouldBeOfType<DisableTwoFactorOutcome.Disabled>();
        await identity.Users.Received(1).SetTwoFactorEnabledAsync(user, false);
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task DisableRejectionReportsFailureWithoutRefreshingCookieAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        identity.Users.SetTwoFactorEnabledAsync(user, false).Returns(IdentityResult.Failed());
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.DisableAsync(user);

        result.Value.ShouldBeOfType<DisableTwoFactorOutcome.DisableFailed>();
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Fact]
    public async Task RecoveryCodesRequireTwoFactorEnabledBeforeGeneratingAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetTwoFactorEnabledAsync(user).Returns(false);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.GenerateRecoveryCodesAsync(user);

        result.Value.ShouldBeOfType<RecoveryCodesOutcome.TwoFactorNotEnabled>();
        await identity.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RecoveryCodesRequestTenAndPreserveGeneratedValuesAsync(int generatedCount)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        string[] expectedCodes = generatedCount == 1 ? ["first-code"] : _recoveryCodes;
        identity.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns(expectedCodes);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.GenerateRecoveryCodesAsync(user);

        result.Value.ShouldBeOfType<RecoveryCodesOutcome.Generated>().Codes.ShouldBe(expectedCodes);
        await identity.Users.Received(1).GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRecoveryCodesAreGenerationFailureAsync(bool empty)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetTwoFactorEnabledAsync(user).Returns(true);
        identity.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns(empty ? [] : (IEnumerable<string>?)null);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.GenerateRecoveryCodesAsync(user);

        result.Value.ShouldBeOfType<RecoveryCodesOutcome.GenerationFailed>();
        await identity.Users.Received(1).GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
    }
}

