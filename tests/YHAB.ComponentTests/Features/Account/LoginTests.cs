using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Pages;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class LoginTests
{
    [Fact]
    public async Task PasswordLoginValidatesRequiredFieldsBeforeCallingIdentityAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var component = account.Render<Login>(context);

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find("[role='alert']").TextContent.ShouldContain("Email"));
        await account.SignIn.DidNotReceiveWithAnyArgs().PasswordSignInAsync(default(string)!, default!, default, default);
    }

    [Fact]
    public async Task PasskeyLoginBypassesPasswordValidationAndNavigatesToReturnUrlAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Login?returnUrl=%2Fevents");
        account.SignIn.PasskeySignInAsync("credential").Returns(SignInResult.Success);
        var component = account.Render<Login>(context);
        GetInput(component.Instance).Passkey = new PasskeyInputModel { CredentialJson = "credential" };

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => navigation.Uri.ShouldBe("http://localhost/events"));
        await account.SignIn.Received(1).PasskeySignInAsync("credential");
        await account.SignIn.DidNotReceiveWithAnyArgs().PasswordSignInAsync(default(string)!, default!, default, default);
        component.FindAll(".validation-message").ShouldBeEmpty();
    }

    [Fact]
    public async Task BrowserErrorTakesPrecedenceOverCredentialAndStopsSignInAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var component = account.Render<Login>(context);
        GetInput(component.Instance).Passkey = new PasskeyInputModel { CredentialJson = "credential", Error = "Browser rejected request" };

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldContain("Browser rejected request"));
        await account.SignIn.DidNotReceiveWithAnyArgs().PasskeySignInAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().PasswordSignInAsync(default(string)!, default!, default, default);
    }

    [Fact]
    public async Task PasswordLoginPreservesRememberMeAndReturnUrlWhenTwoFactorIsRequiredAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Login?returnUrl=%2Fevents");
        account.SignIn.PasswordSignInAsync("member@example.test", "password", true, false).Returns(SignInResult.TwoFactorRequired);
        var component = account.Render<Login>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });
        await component.Find("input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true });

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/LoginWith2fa?returnUrl=%2Fevents&rememberMe=True");
        await account.SignIn.Received(1).PasswordSignInAsync("member@example.test", "password", true, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedPasswordLoginShowsGenericFailureWithoutNavigatingAsync(bool notAllowed)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Login");
        account.SignIn.PasswordSignInAsync("member@example.test", "password", false, false)
            .Returns(notAllowed ? SignInResult.NotAllowed : SignInResult.Failed);
        var component = account.Render<Login>(context);
        await component.Find("input[name='Input.Email']").ChangeAsync(new ChangeEventArgs { Value = "member@example.test" });
        await component.Find("input[name='Input.Password']").ChangeAsync(new ChangeEventArgs { Value = "password" });

        await component.Find("form").SubmitAsync();

        await component.WaitForAssertionAsync(() => component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Error: Invalid login attempt."));
        navigation.Uri.ShouldBe("http://localhost/Account/Login");
    }

    // Static SSR normally maps the JavaScript passkey fields; bUnit does not run that form mapper.
    private static LoginInputModel GetInput(Login page) =>
        (LoginInputModel)typeof(Login).GetProperty("Input", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
}
