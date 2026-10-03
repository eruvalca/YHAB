using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Models;

namespace YHAB.UnitTests.Features.Account.Extensions;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class SignInResultExtensionsTests
{
    [Fact]
    public void ToSignInOutcomePreservesEveryIdentityOutcome()
    {
        SignInResult.Success.ToSignInOutcome().Value.ShouldBeOfType<SignInOutcome.Succeeded>();
        SignInResult.TwoFactorRequired.ToSignInOutcome().Value.ShouldBeOfType<SignInOutcome.RequiresTwoFactor>();
        SignInResult.LockedOut.ToSignInOutcome().Value.ShouldBeOfType<SignInOutcome.LockedOut>();
        SignInResult.NotAllowed.ToSignInOutcome().Value.ShouldBeOfType<SignInOutcome.NotAllowed>();
        SignInResult.Failed.ToSignInOutcome().Value.ShouldBeOfType<SignInOutcome.Failed>();
    }

    [Theory]
    [InlineData(true, true, true, true, typeof(SignInOutcome.Succeeded))]
    [InlineData(false, true, true, true, typeof(SignInOutcome.RequiresTwoFactor))]
    [InlineData(false, false, true, true, typeof(SignInOutcome.LockedOut))]
    [InlineData(false, false, false, true, typeof(SignInOutcome.NotAllowed))]
    [InlineData(false, false, false, false, typeof(SignInOutcome.Failed))]
    public void CombinedFlagsRespectSignInPrecedence(
        bool succeeded, bool requiresTwoFactor, bool isLockedOut, bool isNotAllowed, Type expectedCase)
    {
        var result = CombinedSignInResult.Create(succeeded, requiresTwoFactor, isLockedOut, isNotAllowed);

        result.ToSignInOutcome().Value.GetType().ShouldBe(expectedCase);
    }

    private sealed class CombinedSignInResult : SignInResult
    {
        public static CombinedSignInResult Create(bool succeeded, bool requiresTwoFactor, bool isLockedOut, bool isNotAllowed) =>
            new CombinedSignInResult
            {
                Succeeded = succeeded,
                RequiresTwoFactor = requiresTwoFactor,
                IsLockedOut = isLockedOut,
                IsNotAllowed = isNotAllowed,
            };
    }
}
