using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class IdentityCancellationTests
{
    [Fact]
    public async Task ManagerForwardsScopedTokenAndIndependentScopesDoNotInheritItAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var user = new ApplicationUser();
        store.FindByIdAsync("member", Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<CancellationToken>().ThrowIfCancellationRequested();
            return user;
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IdentityCancellation>();
        services.AddSingleton(store);
        services.AddIdentityCore<ApplicationUser>().AddUserManager<CancellableUserManager>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IdentityCancellation>().Token = cancellation.Token;
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        (await manager.FindByIdAsync("member")).ShouldBeSameAs(user);
        await store.Received(1).FindByIdAsync("member", cancellation.Token);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => manager.FindByIdAsync("member"));

        await using var independent = provider.CreateAsyncScope();
        independent.ServiceProvider.GetRequiredService<IdentityCancellation>().Token.CanBeCanceled.ShouldBeFalse();
        (await independent.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync("member")).ShouldBeSameAs(user);
    }

    [Fact]
    public async Task CanceledRegistrationSkipsStoresAndCreationAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.Cancellation.Token = new CancellationToken(canceled: true);
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        await Should.ThrowAsync<OperationCanceledException>(() => service.PasswordAsync("member@example.test", "secret"));

        identity.Store.ReceivedCalls().ShouldBeEmpty();
        identity.Users.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task RegistrationForwardsCancellationToInitializationAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.Cancellation.Token = TestContext.Current.CancellationToken;
        identity.Users.SupportsUserEmail.Returns(true);
        identity.Users.CreateAsync(Arg.Any<ApplicationUser>(), "secret").Returns(IdentityResult.Success);
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        var result = await service.PasswordAsync("member@example.test", "secret");

        result.Value.ShouldBeOfType<RegistrationOutcome.Created>();
        await identity.Store.Received(1).SetUserNameAsync(Arg.Any<ApplicationUser>(), "member@example.test", TestContext.Current.CancellationToken);
        await identity.Store.Received(1).SetEmailAsync(Arg.Any<ApplicationUser>(), "member@example.test", TestContext.Current.CancellationToken);
        identity.Cancellation.Token.CanBeCanceled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmailChangeCompletesFollowupWritesAfterFirstCommitAsync(bool usernameSucceeds)
    {
        using var identity = IdentityTestContext.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        identity.Cancellation.Token = cancellation.Token;
        var user = new ApplicationUser();
        identity.Users.ChangeEmailAsync(user, "new@example.test", "code").Returns(async _ =>
        {
            await cancellation.CancelAsync();
            return IdentityResult.Success;
        });
        identity.Users.SetUserNameAsync(user, "new@example.test").Returns(_ =>
        {
            identity.Cancellation.Token.CanBeCanceled.ShouldBeFalse();
            return usernameSucceeds ? IdentityResult.Success : IdentityResult.Failed(new IdentityError { Code = "Conflict" });
        });
        var service = new AccountEmailChangeService(identity.Users, identity.SignIn, identity.Cancellation);

        var result = await service.ChangeAsync(user, "new@example.test", "code");

        if (usernameSucceeds)
        {
            result.Value.ShouldBeOfType<EmailChangeOutcome.Changed>();
            await identity.SignIn.Received(1).RefreshSignInAsync(user);
        }
        else
        {
            result.Value.ShouldBeOfType<EmailChangeOutcome.UsernameChangeRejected>().Errors.Single().Code.ShouldBe("Conflict");
            await identity.SignIn.DidNotReceiveWithAnyArgs().RefreshSignInAsync(default!);
        }
    }
}
