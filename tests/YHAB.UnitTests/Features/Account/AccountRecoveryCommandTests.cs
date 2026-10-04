using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountRecoveryCommandTests
{
    [Theory]
    [InlineData("http://budget.example.test")]
    [InlineData("https://budget.example.test/path")]
    [InlineData("https://budget.example.test?code=other")]
    [InlineData("https://budget.example.test#fragment")]
    [InlineData("https://user:password@budget.example.test")]
    [InlineData("not a URL")]
    public async Task InvalidOriginsAreRejectedBeforeAccessingIdentityAsync(string origin)
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        (await AccountRecoveryCommand.ExecuteAsync(provider, "member", origin, output, TestContext.Current.CancellationToken)).ShouldBe(2);

        output.ToString().ShouldNotContain("code=");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyAnExactExistingAccountProducesAnEncodedResetLinkAsync(bool exists)
    {
        using var identity = IdentityTestContext.Create();
        var user = new ApplicationUser { Id = "member", EmailConfirmed = false };
        identity.Users.FindByIdAsync("member").Returns(exists ? user : null);
        identity.Users.GeneratePasswordResetTokenAsync(user).Returns("token+/=");
        var services = new ServiceCollection();
        services.AddSingleton(identity.Users);
        services.AddSingleton(identity.Cancellation);
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await AccountRecoveryCommand.ExecuteAsync(provider, "member", "https://budget.example.test:443/", output, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(exists ? 0 : 1);
        if (exists)
        {
            output.ToString().Trim().ShouldBe("https://budget.example.test/Account/ResetPassword?code=dG9rZW4rLz0");
            await identity.Users.Received(1).GeneratePasswordResetTokenAsync(user);
        }
        else
        {
            output.ToString().ShouldNotContain("code=");
            await identity.Users.DidNotReceiveWithAnyArgs().GeneratePasswordResetTokenAsync(default!);
        }
        identity.Cancellation.Token.ShouldBe(TestContext.Current.CancellationToken);
        await identity.Users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await identity.Users.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default!);
    }
}
