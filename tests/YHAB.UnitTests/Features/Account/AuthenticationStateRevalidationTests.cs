using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Claims;
using Bogus;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "The provider owns each substituted scope; tests verify it disposes scopes asynchronously.")]
public sealed class AuthenticationStateRevalidationTests
{
    [Theory]
    [InlineData("stamp", "stamp", true)]
    [InlineData("stamp", "changed", false)]
    [InlineData("stamp", "STAMP", false)]
    [InlineData(null, "stamp", false)]
    public async Task SecurityStampMustMatchConfiguredClaimExactlyAsync(string? claimStamp, string storedStamp, bool expected)
    {
        using var identity = IdentityTestContext.Create();
        var options = new IdentityOptions();
        options.ClaimsIdentity.SecurityStampClaimType = "yhab:security-stamp";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claimStamp is null ? [] : [new Claim("yhab:security-stamp", claimStamp)], "test"));
        var user = new Faker<ApplicationUser>().UseSeed(147).RuleFor(u => u.Id, f => f.Random.Guid().ToString()).Generate();
        identity.Users.GetUserAsync(principal).Returns(user);
        identity.Users.SupportsUserSecurityStamp.Returns(true);
        identity.Users.GetSecurityStampAsync(user).Returns(storedStamp);
        var scopes = Substitute.For<IServiceScopeFactory>();
        var scope = CreateScope(identity.Users);
        scopes.CreateScope().Returns(scope);
        using var provider = new IdentityRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, scopes, Options.Create(options));

        var valid = await ValidateAsync(provider, principal);

        valid.ShouldBe(expected);
        await identity.Users.Received(1).GetSecurityStampAsync(user);
        await ((IAsyncDisposable)scope).Received(1).DisposeAsync();
    }

    [Fact]
    public async Task MissingUserInvalidatesSessionWithoutReadingStampAsync()
    {
        using var identity = IdentityTestContext.Create();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "deleted-user")], "test"));
        identity.Users.GetUserAsync(principal).Returns((ApplicationUser?)null);
        var scopes = Substitute.For<IServiceScopeFactory>();
        var scope = CreateScope(identity.Users);
        scopes.CreateScope().Returns(scope);
        using var provider = new IdentityRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, scopes, Options.Create(new IdentityOptions()));

        (await ValidateAsync(provider, principal)).ShouldBeFalse();

        await identity.Users.DidNotReceiveWithAnyArgs().GetSecurityStampAsync(default!);
        await ((IAsyncDisposable)scope).Received(1).DisposeAsync();
    }

    [Fact]
    public async Task StoreWithoutSecurityStampsAcceptsExistingUserWithoutReadingStampAsync()
    {
        using var identity = IdentityTestContext.Create();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        identity.Users.GetUserAsync(principal).Returns(new ApplicationUser());
        identity.Users.SupportsUserSecurityStamp.Returns(false);
        var scopes = Substitute.For<IServiceScopeFactory>();
        var scope = CreateScope(identity.Users);
        scopes.CreateScope().Returns(scope);
        using var provider = new IdentityRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, scopes, Options.Create(new IdentityOptions()));

        (await ValidateAsync(provider, principal)).ShouldBeTrue();

        await identity.Users.DidNotReceiveWithAnyArgs().GetSecurityStampAsync(default!);
    }

    [Fact]
    public async Task EachRevalidationUsesFreshScopeAndObservesDeletedUserAsync()
    {
        using var firstIdentity = IdentityTestContext.Create();
        using var secondIdentity = IdentityTestContext.Create();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        firstIdentity.Users.GetUserAsync(principal).Returns(new ApplicationUser());
        firstIdentity.Users.SupportsUserSecurityStamp.Returns(false);
        secondIdentity.Users.GetUserAsync(principal).Returns((ApplicationUser?)null);
        var firstScope = CreateScope(firstIdentity.Users);
        var secondScope = CreateScope(secondIdentity.Users);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(firstScope, secondScope);
        using var provider = new IdentityRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, scopes, Options.Create(new IdentityOptions()));

        (await ValidateAsync(provider, principal)).ShouldBeTrue();
        (await ValidateAsync(provider, principal)).ShouldBeFalse();

        scopes.Received(2).CreateScope();
        await ((IAsyncDisposable)firstScope).Received(1).DisposeAsync();
        await ((IAsyncDisposable)secondScope).Received(1).DisposeAsync();
    }

    [Fact]
    public async Task FailedUserLookupStillDisposesScopeAsync()
    {
        using var identity = IdentityTestContext.Create();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        var failure = new InvalidOperationException("User lookup failed");
        identity.Users.GetUserAsync(principal).Returns(Task.FromException<ApplicationUser?>(failure));
        var scopes = Substitute.For<IServiceScopeFactory>();
        var scope = CreateScope(identity.Users);
        scopes.CreateScope().Returns(scope);
        using var provider = new IdentityRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, scopes, Options.Create(new IdentityOptions()));

        (await Should.ThrowAsync<InvalidOperationException>(() => ValidateAsync(provider, principal))).ShouldBeSameAs(failure);

        await ((IAsyncDisposable)scope).Received(1).DisposeAsync();
    }

    private static IServiceScope CreateScope(UserManager<ApplicationUser> users)
    {
        var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(UserManager<ApplicationUser>)).Returns(users);
        scope.ServiceProvider.Returns(services);
        return scope;
    }

    private static Task<bool> ValidateAsync(IdentityRevalidatingAuthenticationStateProvider provider, ClaimsPrincipal principal)
    {
        // Invoke the framework's protected validation hook directly. Starting the public
        // authentication loop would wait 30 minutes and test the framework's timer.
        var validate = typeof(IdentityRevalidatingAuthenticationStateProvider)
            .GetMethod("ValidateAuthenticationStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<AuthenticationState, CancellationToken, Task<bool>>>(provider);
        return validate(new AuthenticationState(principal), TestContext.Current.CancellationToken);
    }
}
