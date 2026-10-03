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
public sealed class AccountTwoFactorEnableTests
{
    private static readonly string[] _recoveryCodes = ["first-code", "second-code"];

    [Fact]
    public async Task InvalidCodeUsesConfiguredProviderAndNormalizedInputWithoutMutatingAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.Options.Tokens.AuthenticatorTokenProvider = "custom-provider";
        identity.Users.VerifyTwoFactorTokenAsync(user, "custom-provider", "123456").Returns(false);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.EnableAsync(user, "12 3-45 6");

        result.Value.ShouldBeOfType<EnableAuthenticatorOutcome.InvalidCode>();
        await identity.Users.Received(1).VerifyTwoFactorTokenAsync(user, "custom-provider", "123456");
        await identity.Users.DidNotReceiveWithAnyArgs().SetTwoFactorEnabledAsync(default!, default);
        await identity.Users.DidNotReceiveWithAnyArgs().CountRecoveryCodesAsync(default!);
        await identity.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
    }

    [Fact]
    public async Task EnableRejectionStopsBeforeCountingOrGeneratingRecoveryCodesAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        ConfigureValidCode(identity, user);
        identity.Users.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Failed());
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.EnableAsync(user, "12 3-45 6");

        result.Value.ShouldBeOfType<EnableAuthenticatorOutcome.EnableFailed>();
        await identity.Users.DidNotReceiveWithAnyArgs().CountRecoveryCodesAsync(default!);
        await identity.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ExistingRecoveryCodesAreRetainedAfterEnableAsync(int existingCount)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        ConfigureValidCode(identity, user);
        identity.Users.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Success);
        identity.Users.CountRecoveryCodesAsync(user).Returns(existingCount);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.EnableAsync(user, "12 3-45 6");

        result.Value.ShouldBeOfType<EnableAuthenticatorOutcome.Enabled>();
        await identity.Users.Received(1).SetTwoFactorEnabledAsync(user, true);
        await identity.Users.DidNotReceiveWithAnyArgs().GenerateNewTwoFactorRecoveryCodesAsync(default!, default);
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EnableWithoutRecoveryCodesRequestsTenAndReturnsGeneratedValuesAsync(int generatedCount)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        ConfigureValidCode(identity, user);
        identity.Users.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Success);
        identity.Users.CountRecoveryCodesAsync(user).Returns(0);
        string[] expectedCodes = generatedCount == 1 ? ["first-code"] : _recoveryCodes;
        identity.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns(expectedCodes);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.EnableAsync(user, "12 3-45 6");

        result.Value.ShouldBeOfType<EnableAuthenticatorOutcome.EnabledWithRecoveryCodes>().Codes.ShouldBe(expectedCodes);
        await identity.Users.Received(1).GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingGeneratedCodesReturnPartialEnableFailureAsync(bool empty)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        ConfigureValidCode(identity, user);
        identity.Users.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Success);
        identity.Users.CountRecoveryCodesAsync(user).Returns(0);
        identity.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).Returns(empty ? [] : (IEnumerable<string>?)null);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.EnableAsync(user, "12 3-45 6");

        result.Value.ShouldBeOfType<EnableAuthenticatorOutcome.EnabledButRecoveryCodesFailed>();
        await identity.Users.Received(1).SetTwoFactorEnabledAsync(user, true);
        await identity.Users.DidNotReceive().SetTwoFactorEnabledAsync(user, false);
        await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
    }

    private static void ConfigureValidCode(IdentityTestContext identity, ApplicationUser user)
    {
        identity.Users.Options.Tokens.AuthenticatorTokenProvider = "custom-provider";
        identity.Users.VerifyTwoFactorTokenAsync(user, "custom-provider", "123456").Returns(true);
    }
}

