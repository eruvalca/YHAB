using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountRegistrationServiceTests
{
    private static readonly string[] _passwordCreationSteps = ["username", "email", "create-password"];
    private static readonly string[] _externalCreationSteps = ["username", "email", "create", "link"];
    private static readonly string[] _creationErrorCodes = ["EmailTaken", "PasswordWeak"];
    private static readonly string[] _creationErrorDescriptions = ["Email already used", "Password is too weak"];

    [Fact]
    public async Task PasswordCreationInitializesUserBeforeCreatingAndReturnsSameUserAsync()
    {
        using var identity = IdentityTestContext.Create();
        var steps = ConfigureUserInitialization(identity);
        ApplicationUser? createdUser = null;
        identity.Users.CreateAsync(Arg.Any<ApplicationUser>(), "secret").Returns(call =>
        {
            steps.Add("create-password");
            createdUser = call.Arg<ApplicationUser>();
            return IdentityResult.Success;
        });
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        var result = await service.PasswordAsync("member@example.test", "secret");

        var user = result.Value.ShouldBeOfType<RegistrationOutcome.Created>().User;
        user.ShouldBeSameAs(createdUser);
        user.UserName.ShouldBe("member@example.test");
        user.Email.ShouldBe("member@example.test");
        steps.ShouldBe(_passwordCreationSteps);
        await identity.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!);
        await identity.Users.DidNotReceiveWithAnyArgs().AddLoginAsync(default!, default!);
    }

    [Fact]
    public async Task PasswordCreationRejectionPreservesAllErrorsAsync()
    {
        using var identity = IdentityTestContext.Create();
        _ = ConfigureUserInitialization(identity);
        identity.Users.CreateAsync(Arg.Any<ApplicationUser>(), "secret").Returns(RejectedCreation());
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        var result = await service.PasswordAsync("member@example.test", "secret");

        var errors = result.Value.ShouldBeOfType<RegistrationOutcome.CreationRejected>().Errors;
        errors.Select(error => error.Code).ShouldBe(_creationErrorCodes);
        errors.Select(error => error.Description).ShouldBe(_creationErrorDescriptions);
    }

    [Fact]
    public async Task ExternalCreationInitializesThenLinksTheCreatedUserAsync()
    {
        using var identity = IdentityTestContext.Create();
        var steps = ConfigureUserInitialization(identity);
        ApplicationUser? createdUser = null;
        var login = CreateExternalLogin();
        identity.Users.CreateAsync(Arg.Any<ApplicationUser>()).Returns(call =>
        {
            steps.Add("create");
            createdUser = call.Arg<ApplicationUser>();
            return IdentityResult.Success;
        });
        identity.Users.AddLoginAsync(Arg.Any<ApplicationUser>(), login).Returns(call =>
        {
            steps.Add("link");
            call.Arg<ApplicationUser>().ShouldBeSameAs(createdUser);
            return IdentityResult.Success;
        });
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        var result = await service.ExternalAsync("member@example.test", login);

        var user = result.Value.ShouldBeOfType<RegistrationOutcome.Created>().User;
        user.ShouldBeSameAs(createdUser);
        user.UserName.ShouldBe("member@example.test");
        user.Email.ShouldBe("member@example.test");
        steps.ShouldBe(_externalCreationSteps);
        await identity.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!);
    }

    [Fact]
    public async Task ExternalCreationRejectionPreservesErrorsAndSkipsLinkingAsync()
    {
        using var identity = IdentityTestContext.Create();
        _ = ConfigureUserInitialization(identity);
        identity.Users.CreateAsync(Arg.Any<ApplicationUser>()).Returns(RejectedCreation());
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        var result = await service.ExternalAsync("member@example.test", CreateExternalLogin());

        var errors = result.Value.ShouldBeOfType<RegistrationOutcome.CreationRejected>().Errors;
        errors.Select(error => error.Code).ShouldBe(_creationErrorCodes);
        errors.Select(error => error.Description).ShouldBe(_creationErrorDescriptions);
        await identity.Users.DidNotReceiveWithAnyArgs().AddLoginAsync(default!, default!);
    }

    [Fact]
    public async Task LinkRejectionReturnsCreatedUserAndErrorsWithoutDeletingAccountAsync()
    {
        using var identity = IdentityTestContext.Create();
        _ = ConfigureUserInitialization(identity);
        ApplicationUser? createdUser = null;
        identity.Users.CreateAsync(Arg.Any<ApplicationUser>()).Returns(call =>
        {
            createdUser = call.Arg<ApplicationUser>();
            return IdentityResult.Success;
        });
        identity.Users.AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<UserLoginInfo>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = "ProviderConflict", Description = "Provider is already linked" }));
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        var result = await service.ExternalAsync("member@example.test", CreateExternalLogin());

        var failure = result.Value.ShouldBeOfType<ExternalRegistrationOutcome.ExternalLoginLinkFailed>();
        failure.User.ShouldBeSameAs(createdUser);
        failure.User.Email.ShouldBe("member@example.test");
        var error = failure.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("ProviderConflict");
        error.Description.ShouldBe("Provider is already linked");
        await identity.Users.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
        await identity.Users.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!);
        await identity.SignIn.DidNotReceiveWithAnyArgs().SignInAsync(default!, default(bool), default!);
    }

    [Fact]
    public async Task UnsupportedEmailStoreIsAnUnexpectedConfigurationErrorAsync()
    {
        using var identity = IdentityTestContext.Create();
        identity.Users.SupportsUserEmail.Returns(false);
        var service = new AccountRegistrationService(identity.Users, identity.Store, identity.Cancellation);

        await Should.ThrowAsync<NotSupportedException>(() => service.PasswordAsync("member@example.test", "secret"));

        await identity.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!);
    }

    private static List<string> ConfigureUserInitialization(IdentityTestContext identity)
    {
        var steps = new List<string>();
        identity.Users.SupportsUserEmail.Returns(true);
        identity.Store.SetUserNameAsync(Arg.Any<ApplicationUser>(), "member@example.test", CancellationToken.None).Returns(call =>
        {
            steps.Add("username");
            call.Arg<ApplicationUser>().UserName = call.Arg<string>();
            return Task.CompletedTask;
        });
        identity.Store.SetEmailAsync(Arg.Any<ApplicationUser>(), "member@example.test", CancellationToken.None).Returns(call =>
        {
            steps.Add("email");
            call.Arg<ApplicationUser>().Email = call.Arg<string>();
            return Task.CompletedTask;
        });
        return steps;
    }

    private static IdentityResult RejectedCreation() => IdentityResult.Failed(
        new IdentityError { Code = "EmailTaken", Description = "Email already used" },
        new IdentityError { Code = "PasswordWeak", Description = "Password is too weak" });

    private static ExternalLoginInfo CreateExternalLogin() => new(new ClaimsPrincipal(), "Provider", "key", "Provider");
}
