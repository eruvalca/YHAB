using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Components;
using YHAB.Features.Account.Models;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class PasskeySubmitTests
{
    [Theory]
    [InlineData(PasskeyOperation.Create, null)]
    [InlineData(PasskeyOperation.Request, "Input.Email")]
    public async Task SubmitConnectsOperationAndInputNamesToAntiforgeryHeaderAsync(PasskeyOperation operation, string? emailName)
    {
        await using var context = new BunitContext();
        var http = new DefaultHttpContext();
        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.GetTokens(http).Returns(new AntiforgeryTokenSet("request&token", "cookie-token", "__token", "X-CSRF"));
        context.Services.AddSingleton(antiforgery);

        var component = context.Render<PasskeySubmit>(parameters => parameters
            .AddCascadingValue<HttpContext>(http)
            .Add(submit => submit.Operation, operation)
            .Add(submit => submit.Name, "Input.Passkey")
            .Add(submit => submit.EmailName, emailName)
            .Add(submit => submit.AdditionalAttributes, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["class"] = "primary-action",
                ["aria-label"] = "Use a passkey",
                ["disabled"] = true,
            })
            .AddChildContent("Continue with passkey"));

        var bridge = component.Find("passkey-submit");
        bridge.GetAttribute("operation").ShouldBe(operation.ToString());
        bridge.GetAttribute("name").ShouldBe("Input.Passkey");
        bridge.GetAttribute("email-name").ShouldBe(emailName);
        bridge.GetAttribute("request-token-name").ShouldBe("X-CSRF");
        bridge.GetAttribute("request-token-value").ShouldBe("request&token");
        component.Markup.ShouldNotContain("cookie-token");
        var button = component.Find("button[type='submit'][name='__passkeySubmit']");
        button.TextContent.ShouldBe("Continue with passkey");
        button.GetAttribute("class").ShouldBe("primary-action");
        button.GetAttribute("aria-label").ShouldBe("Use a passkey");
        button.HasAttribute("disabled").ShouldBeTrue();
        antiforgery.Received(1).GetTokens(http);
    }

    [Fact]
    public async Task MissingAntiforgeryServiceOmitsTokenAttributesWithoutPreventingRenderingAsync()
    {
        await using var context = new BunitContext();

        var component = context.Render<PasskeySubmit>(parameters => parameters
            .AddCascadingValue<HttpContext>(new DefaultHttpContext())
            .Add(submit => submit.Operation, PasskeyOperation.Create)
            .Add(submit => submit.Name, "Input")
            .AddChildContent("Add passkey"));

        var bridge = component.Find("passkey-submit");
        bridge.HasAttribute("request-token-name").ShouldBeFalse();
        bridge.HasAttribute("request-token-value").ShouldBeFalse();
        bridge.HasAttribute("email-name").ShouldBeFalse();
        component.Find("button").TextContent.ShouldBe("Add passkey");
    }
}
