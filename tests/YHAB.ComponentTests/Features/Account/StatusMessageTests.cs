using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Components;
using YHAB.Features.Account.Services;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class StatusMessageTests
{
    [Theory]
    [InlineData("Error: Cannot save <script>alert('x')</script>", "error")]
    [InlineData("Profile saved & confirmed", "success")]
    public async Task CookieMessageIsDecodedRenderedAsTextAndExpiredAsync(string message, string kind)
    {
        await using var context = new BunitContext();
        var http = CreateHttpWithMessage(message);

        var component = context.Render<StatusMessage>(parameters => parameters.AddCascadingValue<HttpContext>(http));

        component.Find("[role='alert']").TextContent.ShouldBe(message);
        component.Find("[role='alert']").GetAttribute("data-kind").ShouldBe(kind);
        component.FindAll("script").ShouldBeEmpty();
        var deletion = SetCookieHeaderValue.Parse(http.Response.Headers.SetCookie.ToString());
        deletion.Name.ToString().ShouldBe(IdentityRedirectManager.StatusCookieName);
        deletion.Value.ToString().ShouldBeEmpty();
        deletion.Expires.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(DateTimeOffset.UnixEpoch);
    }

    [Theory]
    [InlineData("New status")]
    [InlineData("")]
    public async Task ExplicitMessageOverridesAndConsumesCookieEvenWhenEmptyAsync(string message)
    {
        await using var context = new BunitContext();
        var http = CreateHttpWithMessage("Old status");

        var component = context.Render<StatusMessage>(parameters => parameters
            .AddCascadingValue<HttpContext>(http).Add(status => status.Message, message));

        component.Markup.ShouldNotContain("Old status");
        if (string.IsNullOrEmpty(message))
        {
            component.FindAll("[role='alert']").ShouldBeEmpty();
        }
        else
        {
            component.Find("[role='alert']").TextContent.ShouldBe(message);
        }
        SetCookieHeaderValue.Parse(http.Response.Headers.SetCookie.ToString()).Name.ToString()
            .ShouldBe(IdentityRedirectManager.StatusCookieName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingMessageDoesNotRenderAlertOrWriteCookiesAsync(string? message)
    {
        await using var context = new BunitContext();
        var http = new DefaultHttpContext();

        var component = context.Render<StatusMessage>(parameters => parameters
            .AddCascadingValue<HttpContext>(http).Add(status => status.Message, message));

        component.FindAll("[role='alert']").ShouldBeEmpty();
        http.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdatingMessageReplacesSeverityAndEncodesMarkupWithoutAStatusCookieAsync()
    {
        await using var context = new BunitContext();
        var http = new DefaultHttpContext();
        var component = context.Render<StatusMessage>(parameters => parameters
            .AddCascadingValue<HttpContext>(http).Add(status => status.Message, "Saved"));

        component.Render(parameters => parameters.Add(status => status.Message, "Error: <b>Denied</b>"));

        component.Find("[data-kind='error']").TextContent.ShouldBe("Error: <b>Denied</b>");
        component.FindAll("b").ShouldBeEmpty();
        component.FindAll("[data-kind='success']").ShouldBeEmpty();
        http.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    private static DefaultHttpContext CreateHttpWithMessage(string message)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{IdentityRedirectManager.StatusCookieName}={Uri.EscapeDataString(message)}";
        return http;
    }
}
