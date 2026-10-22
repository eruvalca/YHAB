using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Diagnostics;
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
                component.Markup.ShouldNotContain("mention request");
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

    [Fact]
    public async Task ReplacesEnvironmentGuidanceWithBrandedRecoveryActionsAsync()
    {
        await using var context = new BunitContext();
        var component = context.Render<Error>(parameters =>
            parameters.AddCascadingValue<HttpContext>(new DefaultHttpContext()));

        component.Find("h1").TextContent.ShouldBe("Something went wrong.");
        component.Find("p.lead").TextContent.ShouldContain("Please try again");
        var retry = component.Find("a.primary-action");
        retry.TextContent.ShouldBe("Try again");
        retry.GetAttribute("href").ShouldBe("/");
        var actions = component.FindAll("a.secondary-action");
        actions.Select(action => action.TextContent).ShouldBe(["Return home", "Open your plans"]);
        actions.Select(action => action.GetAttribute("href")).ShouldBe(["/", "/plans"]);
        component.Markup.ShouldNotContain("Development");
        component.Markup.ShouldNotContain("ASPNETCORE_ENVIRONMENT");
    }

    [Fact]
    public async Task RetryTargetsOriginalPathFromExceptionHandlerAsync()
    {
        await using var context = new BunitContext();
        var http = new DefaultHttpContext();
        http.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
        {
            Path = "/plans/failing-plan",
            Error = new InvalidOperationException("boom"),
        });

        var component = context.Render<Error>(parameters =>
            parameters.AddCascadingValue<HttpContext>(http));

        component.Find("a.primary-action").GetAttribute("href").ShouldBe("/plans/failing-plan");
    }
}
