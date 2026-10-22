using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Components;
using YHAB.Features.Account.Pages;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ExternalProviderComponentsTests
{
    [Fact]
    public async Task PickerWithoutProvidersRendersNothingAndCannotSubmitAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        var component = context.Render<ExternalLoginPicker>(parameters => parameters
            .AddCascadingValue<HttpContext>(account.Http)
            .Add(picker => picker.Heading, "Use another service to log in."));

        component.FindAll("section, h3, form, button[name='provider']").ShouldBeEmpty();
        component.Markup.ShouldNotContain("external authentication services configured.");
        await account.SignIn.Received(1).GetExternalAuthenticationSchemesAsync();
    }

    [Fact]
    public async Task LoginAndRegisterOmitExternalProviderCopyWhenNoneConfiguredAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        var login = account.Render<Login>(context);
        login.Markup.ShouldNotContain("external authentication services configured.");
        login.FindAll("h3").Any(heading => string.Equals(heading.TextContent, "Use another service to log in.", StringComparison.Ordinal)).ShouldBeFalse();

        var register = account.Render<Register>(context);
        register.Markup.ShouldNotContain("external authentication services configured.");
        register.FindAll("h3").Any(heading => string.Equals(heading.TextContent, "Use another service to register.", StringComparison.Ordinal)).ShouldBeFalse();
        await account.SignIn.Received(2).GetExternalAuthenticationSchemesAsync();
    }

    [Fact]
    public async Task PickerPostsSchemeNamesPreservesDisplayNamesAndDecodesReturnUrlOnceAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        AuthenticationScheme[] schemes =
        [
            new("oidc-primary", "Work <account>", typeof(AuthenticationHandler<AuthenticationSchemeOptions>)),
            new("social-secondary", "Personal & friends", typeof(AuthenticationHandler<AuthenticationSchemeOptions>)),
        ];
        account.SignIn.GetExternalAuthenticationSchemesAsync().Returns(schemes);
        const string ReturnUrl = "/events?category=a&next=%2Fprofile#details";
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"Account/Login?ReturnUrl={Uri.EscapeDataString(ReturnUrl)}");

        var component = context.Render<ExternalLoginPicker>(parameters => parameters
            .AddCascadingValue<HttpContext>(account.Http)
            .Add(picker => picker.Heading, "Use another service to log in."));

        component.Find("h3").TextContent.ShouldBe("Use another service to log in.");
        component.Find("form").GetAttribute("method").ShouldBe("post");
        component.Find("form").GetAttribute("action").ShouldBe("Account/PerformExternalLogin");
        component.Find("input[name='ReturnUrl']").GetAttribute("value").ShouldBe(ReturnUrl);
        var buttons = component.FindAll("button[name='provider']");
        buttons.Select(button => button.GetAttribute("value")).ShouldBe(["oidc-primary", "social-secondary"]);
        buttons.Select(button => button.TextContent).ShouldBe(["Work <account>", "Personal & friends"]);
        buttons.Select(button => button.GetAttribute("title")).ShouldBe(
            ["Log in using your Work <account> account", "Log in using your Personal & friends account"]);
        component.FindAll("account").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManageNavigationOnlyOffersExternalLoginManagementWhenAProviderExistsAsync(bool configured)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.SignIn.GetExternalAuthenticationSchemesAsync().Returns(configured
            ? [new AuthenticationScheme("oidc", "Work account", typeof(AuthenticationHandler<AuthenticationSchemeOptions>))]
            : Array.Empty<AuthenticationScheme>());

        var component = account.Render<ManageNavMenu>(context);

        component.FindAll("a[href='Account/Manage/ExternalLogins']").Count.ShouldBe(configured ? 1 : 0);
        component.Find("a[href='Account/Manage/Passkeys']").TextContent.Trim().ShouldBe("Passkeys");
        await account.SignIn.Received(1).GetExternalAuthenticationSchemesAsync();
    }
}
