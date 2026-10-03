using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;
using YHAB.Features.Errors.Pages;

namespace YHAB.ComponentTests.Features.Errors.Pages;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ErrorTests
{
    [Fact]
    public async Task CurrentActivityIdTakesPrecedenceOverHttpRequestIdAsync()
    {
        await using var context = new BunitContext();
        using var activity = new Activity("failing-request");
        activity.Start();
        var http = new DefaultHttpContext { TraceIdentifier = "http-request-id" };

        var component = context.Render<Error>(parameters => parameters.AddCascadingValue<HttpContext>(http));

        component.Find("code").TextContent.ShouldBe(activity.Id);
        component.Markup.ShouldNotContain("http-request-id");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("request<&>identifier")]
    public async Task WithoutActivityUsesHttpIdOnlyWhenAvailableAndEncodesItAsync(string? requestId)
    {
        await using var context = new BunitContext();
        var previousActivity = Activity.Current;
        try
        {
            Activity.Current = null;
            var component = context.Render<Error>(parameters =>
            {
                if (requestId is not null)
                {
                    parameters.AddCascadingValue<HttpContext>(new DefaultHttpContext { TraceIdentifier = requestId });
                }
            });

            if (string.IsNullOrEmpty(requestId))
            {
                component.FindAll("code").ShouldBeEmpty();
                component.Markup.ShouldNotContain("Request ID:");
            }
            else
            {
                component.Find("code").TextContent.ShouldBe(requestId);
                component.Find("code").Children.ShouldBeEmpty();
            }
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }
}
