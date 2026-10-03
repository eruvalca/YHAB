using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Models;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PasskeySubmissionTests
{
    [Fact]
    public void MissingTransportInputMeansMissingSubmission() =>
        PasskeySubmission.From(null).Value.ShouldBeOfType<PasskeySubmission.Missing>();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void EmptyFieldsMeanMissingSubmission(string? credential, string? error) =>
        PasskeySubmission.From(new PasskeyInputModel { CredentialJson = credential, Error = error })
            .Value.ShouldBeOfType<PasskeySubmission.Missing>();

    [Theory]
    [InlineData("credential", null)]
    [InlineData("credential", "")]
    [InlineData(" ", "")]
    public void NonemptyCredentialPreservesExactValue(string credential, string? error) =>
        PasskeySubmission.From(new PasskeyInputModel { CredentialJson = credential, Error = error })
            .Value.ShouldBeOfType<PasskeySubmission.Credential>().Json.ShouldBe(credential);

    [Theory]
    [InlineData(null, "error")]
    [InlineData("credential", "error")]
    [InlineData("credential", " ")]
    public void NonemptyErrorWinsOverCredentialAndPreservesMessage(string? credential, string error) =>
        PasskeySubmission.From(new PasskeyInputModel { CredentialJson = credential, Error = error })
            .Value.ShouldBeOfType<PasskeySubmission.BrowserError>().Message.ShouldBe(error);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("%%%")]
    public void MissingEmptyAndMalformedCredentialIdsAreInvalid(string? input) =>
        CredentialIdOutcome.Decode(input).Value.ShouldBeOfType<CredentialIdOutcome.InvalidCredentialId>();

    [Fact]
    public void ValidCredentialIdPreservesDecodedBytes() =>
        CredentialIdOutcome.Decode("AQID_w").Value.ShouldBeOfType<CredentialIdOutcome.DecodedCredentialId>()
            .Bytes.ShouldBe(new byte[] { 1, 2, 3, 255 });

    [Fact]
    public void SingleByteCredentialIdIsAccepted() =>
        CredentialIdOutcome.Decode("AQ").Value.ShouldBeOfType<CredentialIdOutcome.DecodedCredentialId>()
            .Bytes.ShouldBe(new byte[] { 1 });
}
