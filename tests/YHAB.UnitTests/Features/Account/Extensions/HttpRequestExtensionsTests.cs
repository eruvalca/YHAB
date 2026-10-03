using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Extensions;

namespace YHAB.UnitTests.Features.Account.Extensions;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class HttpRequestExtensionsTests
{
    [Theory]
    [InlineData("GET", true)]
    [InlineData("get", true)]
    [InlineData("gEt", true)]
    [InlineData("POST", false)]
    [InlineData("HEAD", false)]
    [InlineData("", false)]
    [InlineData(" GET", false)]
    public void IsGetMatchesHttpSemantics(string method, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;

        context.Request.IsGet.ShouldBe(expected);
    }
}
