using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountSignInServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordPreservesRememberMeWithoutCountingFailuresAsync(bool rememberMe)
    {
        using var identity = IdentityTestContext.Create();
        identity.SignIn.PasswordSignInAsync("member@example.test", "secret", rememberMe, false).Returns(SignInResult.Success);
        var service = new AccountSignInService(identity.SignIn);

        var result = await service.PasswordAsync("member@example.test", "secret", rememberMe);

        result.Value.ShouldBeOfType<SignInOutcome.Succeeded>();
        await identity.SignIn.Received(1).PasswordSignInAsync("member@example.test", "secret", rememberMe, false);
    }

    [Fact]
    public async Task PasskeyForwardsCredentialWithoutPasswordSignInAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.SignIn.PasskeySignInAsync("{\"credential\":1}").Returns(SignInResult.NotAllowed);
        var service = new AccountSignInService(identity.SignIn);

        var result = await service.PasskeyAsync("{\"credential\":1}");

        result.Value.ShouldBeOfType<SignInOutcome.NotAllowed>();
        await identity.SignIn.Received(1).PasskeySignInAsync("{\"credential\":1}");
        await identity.SignIn.DidNotReceiveWithAnyArgs().PasswordSignInAsync(default(string)!, default!, default, default);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AuthenticatorRemovesSpacesAndHyphensAndPreservesFlagsAsync(bool rememberMe, bool rememberMachine)
    {
        using var identity = IdentityTestContext.Create();
        identity.SignIn.TwoFactorAuthenticatorSignInAsync("123456", rememberMe, rememberMachine).Returns(SignInResult.LockedOut);
        var service = new AccountSignInService(identity.SignIn);

        var result = await service.AuthenticatorAsync("12 3-45 6", rememberMe, rememberMachine);

        result.Value.ShouldBeOfType<SignInOutcome.LockedOut>();
        await identity.SignIn.Received(1).TwoFactorAuthenticatorSignInAsync("123456", rememberMe, rememberMachine);
    }

    [Fact]
    public async Task RecoveryCodeRemovesSpacesButPreservesHyphensAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.SignIn.TwoFactorRecoveryCodeSignInAsync("ABC-123").Returns(SignInResult.Failed);
        var service = new AccountSignInService(identity.SignIn);

        var result = await service.RecoveryCodeAsync(" AB C-1 23 ");

        result.Value.ShouldBeOfType<SignInOutcome.Failed>();
        await identity.SignIn.Received(1).TwoFactorRecoveryCodeSignInAsync("ABC-123");
    }

    [Fact]
    public async Task ExternalUsesTemporarySignInAndBypassesTwoFactorAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.SignIn.ExternalLoginSignInAsync("Provider", "key", false, true).Returns(SignInResult.NotAllowed);
        var service = new AccountSignInService(identity.SignIn);

        var result = await service.ExternalAsync("Provider", "key");

        result.Value.ShouldBeOfType<SignInOutcome.NotAllowed>();
        await identity.SignIn.Received(1).ExternalLoginSignInAsync("Provider", "key", false, true);
    }

    [Fact]
    public async Task UnexpectedIdentityExceptionPropagatesAsync()
    {
        using var identity = IdentityTestContext.Create();
        var failure = new InvalidOperationException("Unavailable identity service");
        identity.SignIn.PasskeySignInAsync("credential").ThrowsAsync(failure);
        var service = new AccountSignInService(identity.SignIn);

        var actual = await Should.ThrowAsync<InvalidOperationException>(() => service.PasskeyAsync("credential"));

        actual.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task IdentityCancellationPropagatesAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.SignIn.PasskeySignInAsync("credential").Returns(Task.FromCanceled<SignInResult>(new CancellationToken(canceled: true)));
        var service = new AccountSignInService(identity.SignIn);

        var operation = service.PasskeyAsync("credential");

        await Should.ThrowAsync<OperationCanceledException>(() => operation);
        operation.IsCanceled.ShouldBeTrue();
    }
}
