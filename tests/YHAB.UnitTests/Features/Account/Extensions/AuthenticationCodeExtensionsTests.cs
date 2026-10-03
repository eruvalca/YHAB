using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Extensions;

namespace YHAB.UnitTests.Features.Account.Extensions;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class AuthenticationCodeExtensionsTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("123456", "123456")]
    [InlineData(" -12 3--45 6- ", "123456")]
    [InlineData(" -- ", "")]
    [InlineData("\t12\r\n3\u00a04–5", "\t12\r\n3\u00a04–5")]
    public void AuthenticatorNormalizationRemovesOnlyLiteralSpacesAndHyphens(string code, string expected)
    {
        ArgumentNullException.ThrowIfNull(code);

        code.NormalizeAuthenticatorCode().ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("ABC-123", "ABC-123")]
    [InlineData(" AB C-1 23 ", "ABC-123")]
    [InlineData(" -- ", "--")]
    [InlineData("   ", "")]
    [InlineData("\tAB\r\nC\u00a01–2", "\tAB\r\nC\u00a01–2")]
    public void RecoveryNormalizationRemovesOnlyLiteralSpaces(string code, string expected)
    {
        ArgumentNullException.ThrowIfNull(code);

        code.NormalizeRecoveryCode().ShouldBe(expected);
    }
}
