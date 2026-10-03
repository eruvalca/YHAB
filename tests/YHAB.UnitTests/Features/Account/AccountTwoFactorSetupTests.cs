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
public sealed class AccountTwoFactorSetupTests
{
    [Fact]
    public async Task ExistingKeyIsPreservedWithoutResetAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetAuthenticatorKeyAsync(user).Returns("EXISTINGKEY");
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.PrepareAsync(user);

        result.Value.ShouldBeOfType<AuthenticatorSetupOutcome.SetupReady>().Key.ShouldBe("EXISTINGKEY");
        await identity.Users.DidNotReceiveWithAnyArgs().ResetAuthenticatorKeyAsync(default!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingKeyIsInitializedAndReloadedAsync(string? original)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetAuthenticatorKeyAsync(user).Returns(original, "NEWKEY");
        identity.Users.ResetAuthenticatorKeyAsync(user).Returns(IdentityResult.Success);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.PrepareAsync(user);

        result.Value.ShouldBeOfType<AuthenticatorSetupOutcome.SetupReady>().Key.ShouldBe("NEWKEY");
        await identity.Users.Received(1).ResetAuthenticatorKeyAsync(user);
        await identity.Users.Received(2).GetAuthenticatorKeyAsync(user);
    }

    [Fact]
    public async Task KeyInitializationRejectionStopsWithoutReloadingAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetAuthenticatorKeyAsync(user).Returns((string?)null);
        identity.Users.ResetAuthenticatorKeyAsync(user).Returns(IdentityResult.Failed());
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.PrepareAsync(user);

        result.Value.ShouldBeOfType<AuthenticatorSetupOutcome.KeyInitializationFailed>();
        await identity.Users.Received(1).GetAuthenticatorKeyAsync(user);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingReloadedKeyIsFailureAfterSuccessfulResetAsync(string? reloaded)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetAuthenticatorKeyAsync(user).Returns((string?)null, reloaded);
        identity.Users.ResetAuthenticatorKeyAsync(user).Returns(IdentityResult.Success);
        var service = new AccountTwoFactorService(identity.Users, identity.SignIn);

        var result = await service.PrepareAsync(user);

        result.Value.ShouldBeOfType<AuthenticatorSetupOutcome.KeyInitializationFailed>();
        await identity.Users.Received(1).ResetAuthenticatorKeyAsync(user);
        await identity.Users.Received(2).GetAuthenticatorKeyAsync(user);
    }
}
