using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Extensions;

namespace YHAB.UnitTests.Features.Account.Extensions;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class IdentityErrorExtensionsTests
{
    [Theory]
    [InlineData(",")]
    [InlineData(", ")]
    public void EmptyErrorsProduceEmptyDescription(string separator)
    {
        IEnumerable<IdentityError> errors = [];

        errors.FormatDescriptions(separator).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(",")]
    [InlineData(", ")]
    public void SingleErrorPreservesDescriptionWithoutSeparator(string separator)
    {
        IEnumerable<IdentityError> errors = [new() { Code = "IgnoredCode", Description = "First, unchanged error." }];

        errors.FormatDescriptions(separator).ShouldBe("First, unchanged error.");
    }

    [Theory]
    [InlineData(",", "Second,First,Second")]
    [InlineData(", ", "Second, First, Second")]
    public void FormatDescriptionsPreservesExactOrderDuplicatesAndSeparator(string separator, string expected)
    {
        IEnumerable<IdentityError> errors =
        [
            new() { Description = "Second" },
            new() { Description = "First" },
            new() { Description = "Second" },
        ];

        errors.FormatDescriptions(separator).ShouldBe(expected);
    }
}
