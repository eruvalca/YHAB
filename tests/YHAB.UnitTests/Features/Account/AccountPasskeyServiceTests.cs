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
public sealed class AccountPasskeyServiceTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    public async Task LimitStopsAttestationAndPersistenceAsync(int count)
    {
        using var identity = IdentityTestContext.Create();
        var service = new AccountPasskeyService(identity.Users, identity.SignIn);

        var result = await service.AddAsync(new ApplicationUser(), "credential", count);

        result.Value.ShouldBeOfType<AddPasskeyOutcome.LimitReached>();
        await identity.SignIn.DidNotReceiveWithAnyArgs().PerformPasskeyAttestationAsync(default!);
        await identity.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task SuccessfulAdditionBelowLimitReturnsCredentialIdAsync(int count)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        var passkey = CreatePasskey();
        identity.SignIn.PerformPasskeyAttestationAsync("credential").Returns(PasskeyAttestationResult.Success(passkey,
            new PasskeyUserEntity { Id = user.Id, Name = "member", DisplayName = "Member" }));
        identity.Users.AddOrUpdatePasskeyAsync(user, passkey).Returns(IdentityResult.Success);
        var service = new AccountPasskeyService(identity.Users, identity.SignIn);

        var result = await service.AddAsync(user, "credential", count);

        result.Value.ShouldBeOfType<AddPasskeyOutcome.Added>().CredentialId.ShouldBe(new byte[] { 1, 2, 3 });
        await identity.Users.Received(1).AddOrUpdatePasskeyAsync(user, passkey);
    }

    [Fact]
    public async Task RejectedAttestationPreservesMessageAndSkipsPersistenceAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.SignIn.PerformPasskeyAttestationAsync("credential").Returns(PasskeyAttestationResult.Fail(new PasskeyException("Rejected origin")));
        var service = new AccountPasskeyService(identity.Users, identity.SignIn);

        var result = await service.AddAsync(new ApplicationUser(), "credential", 1);

        result.Value.ShouldBeOfType<AddPasskeyOutcome.AttestationRejected>().Message.ShouldBe("Rejected origin");
        await identity.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Fact]
    public async Task RejectedPersistencePreservesIdentityErrorCodesAndDescriptionsAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        var passkey = CreatePasskey();
        identity.SignIn.PerformPasskeyAttestationAsync("credential").Returns(PasskeyAttestationResult.Success(passkey,
            new PasskeyUserEntity { Id = user.Id, Name = "member", DisplayName = "Member" }));
        identity.Users.AddOrUpdatePasskeyAsync(user, passkey).Returns(IdentityResult.Failed(new IdentityError { Code = "Conflict", Description = "Concurrent change" }));
        var service = new AccountPasskeyService(identity.Users, identity.SignIn);

        var result = await service.AddAsync(user, "credential", 1);

        var error = result.Value.ShouldBeOfType<AddPasskeyOutcome.PersistenceRejected>().Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("Conflict");
        error.Description.ShouldBe("Concurrent change");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("malformed!")]
    public async Task InvalidLookupSkipsIdentityAsync(string? credential)
    {
        using var identity = IdentityTestContext.Create();
        var service = new AccountPasskeyService(identity.Users, identity.SignIn);

        var result = await service.FindAsync(new ApplicationUser(), credential);

        result.Value.ShouldBeOfType<CredentialIdOutcome.InvalidCredentialId>();
        await identity.Users.DidNotReceiveWithAnyArgs().GetPasskeyAsync(default!, default!);
    }

    [Fact]
    public async Task ValidMissingCredentialReturnsNotFoundAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        identity.Users.GetPasskeyAsync(user, Arg.Any<byte[]>()).Returns((UserPasskeyInfo?)null);
        var service = new AccountPasskeyService(identity.Users, identity.SignIn);

        var result = await service.FindAsync(user, "AQID");

        result.Value.ShouldBeOfType<PasskeyLookupOutcome.NotFound>();
        await identity.Users.Received(1).GetPasskeyAsync(user, Arg.Is<byte[]>(value => value.SequenceEqual(new byte[] { 1, 2, 3 })));
    }

    [Fact]
    public async Task FoundCredentialPreservesPasskeyInstanceAsync()
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser();
        var passkey = CreatePasskey();
        identity.Users.GetPasskeyAsync(user, Arg.Any<byte[]>()).Returns(passkey);
        var service = new AccountPasskeyService(identity.Users, identity.SignIn);

        var result = await service.FindAsync(user, "AQID");

        result.Value.ShouldBeOfType<PasskeyLookupOutcome.Found>().Passkey.ShouldBeSameAs(passkey);
    }

    private static UserPasskeyInfo CreatePasskey() => new([1, 2, 3], [4, 5, 6], DateTimeOffset.UnixEpoch,
        0, ["internal"], true, false, false, [], []);
}
