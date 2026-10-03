using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Models;

namespace YHAB.UnitTests.Features.Account.Extensions;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class IdentityTokenExtensionsTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("a", "YQ")]
    [InlineData("ab", "YWI")]
    [InlineData("abc", "YWJj")]
    [InlineData("token+/é", "dG9rZW4rL8Op")]
    [InlineData("😀", "8J-YgA")]
    [InlineData("\u083f", "4KC_")]
    public void EncodingPreservesExpectedUrlSafeRepresentationAndRoundTrips(string token, string expected)
    {
        ArgumentNullException.ThrowIfNull(token);

        var encoded = token.EncodeIdentityToken();

        encoded.ShouldBe(expected);
        TokenDecodeOutcome.Decode(encoded).Value.ShouldBeOfType<TokenDecodeOutcome.DecodedToken>().Value.ShouldBe(token);
    }
}
