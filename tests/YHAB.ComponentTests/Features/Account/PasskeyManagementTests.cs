using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class PasskeyManagementTests
{
    [Theory]
    [InlineData(false, "Error: The passkey could not be deleted.")]
    [InlineData(true, "Passkey deleted successfully.")]
    public async Task DeleteDecodesSelectedCredentialAndReportsPersistenceResultAsync(bool succeeds, string message)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetPasskeysAsync(user).Returns([CreatePasskey()]);
        account.Users.RemovePasskeyAsync(user, Arg.Any<byte[]>()).Returns(succeeds ? IdentityResult.Success : IdentityResult.Failed());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/Passkeys?old=state");
        var component = account.Render<Passkeys>(context);
        component.Find("input[name='CredentialId']").GetAttribute("value").ShouldBe("-_8A");
        component.Instance.SetFormValue("Action", "delete");
        component.Instance.SetFormValue("CredentialId", "-_8A");

        await component.Find("form").SubmitAsync();

        await account.Users.Received(1).RemovePasskeyAsync(user, Arg.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 251, 255, 0 })));
        account.StatusCookie.ShouldContain(message);
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/Passkeys");
        await account.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Theory]
    [InlineData("rename", "http://localhost/Account/Manage/RenamePasskey/-_8A")]
    [InlineData("unrecognized", "http://localhost/Account/Manage/Passkeys")]
    [InlineData(null, "http://localhost/Account/Manage/Passkeys")]
    public async Task NonDeleteActionsNavigateWithoutChangingCredentialsAsync(string? action, string expectedDestination)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetPasskeysAsync(user).Returns([CreatePasskey()]);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/Passkeys");
        var component = account.Render<Passkeys>(context);
        component.Instance.SetFormValue("Action", action);
        component.Instance.SetFormValue("CredentialId", "-_8A");

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe(expectedDestination);
        if (string.Equals(action, "rename", StringComparison.Ordinal))
        {
            account.StatusCookie.ShouldBeEmpty();
        }
        else
        {
            account.StatusCookie.ShouldContain($"Error: Unknown action '{action}'.");
        }
        await account.Users.DidNotReceiveWithAnyArgs().RemovePasskeyAsync(default!, default!);
        await account.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Fact]
    public async Task MissingUserRedirectsAndCannotAddPostedCredentialAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var component = account.Render<Passkeys>(context);
        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        component.Instance.SetFormValue("Input", new PasskeyInputModel { CredentialJson = "credential" });

        await component.Find("form").SubmitAsync();

        navigation.Uri.ShouldBe("http://localhost/Account/InvalidUser");
        await account.Users.DidNotReceiveWithAnyArgs().GetPasskeysAsync(default!);
        await account.SignIn.DidNotReceiveWithAnyArgs().PerformPasskeyAttestationAsync(default!);
        await account.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Fact]
    public async Task MissingUserCannotRenameEvenAfterValidNameSubmissionAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var component = context.Render<RenamePasskey>(parameters => parameters
            .AddCascadingValue<HttpContext>(account.Http).Add(page => page.Id, "-_8A"));
        await component.Find("input[name='Input.Name']").ChangeAsync(new ChangeEventArgs { Value = "My laptop" });

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/InvalidUser");
        await account.Users.DidNotReceiveWithAnyArgs().GetPasskeyAsync(default!, default!);
        await account.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task RenameRejectsMissingOrOverlongNamesBeforePersistenceAsync(int nameLength)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var passkey = CreatePasskey();
        account.Users.GetPasskeyAsync(user, Arg.Any<byte[]>()).Returns(passkey);
        var component = context.Render<RenamePasskey>(parameters => parameters
            .AddCascadingValue<HttpContext>(account.Http).Add(page => page.Id, "-_8A"));
        await component.Find("input[name='Input.Name']").ChangeAsync(new ChangeEventArgs { Value = new string('x', nameLength) });

        await component.Find("form").SubmitAsync();

        component.Find(".validation-message").TextContent.ShouldBe(nameLength == 0
            ? "The Name field is required."
            : "Passkey names must be no longer than 200 characters.");
        passkey.Name.ShouldBe("Laptop");
        await account.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public async Task RenameAcceptsNamesAtBothLengthBoundariesAsync(int nameLength)
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        var passkey = CreatePasskey();
        account.Users.GetPasskeyAsync(user, Arg.Any<byte[]>()).Returns(passkey);
        account.Users.AddOrUpdatePasskeyAsync(user, passkey).Returns(IdentityResult.Success);
        var component = context.Render<RenamePasskey>(parameters => parameters
            .AddCascadingValue<HttpContext>(account.Http).Add(page => page.Id, "-_8A"));
        var name = new string('x', nameLength);
        await component.Find("input[name='Input.Name']").ChangeAsync(new ChangeEventArgs { Value = name });

        await component.Find("form").SubmitAsync();

        passkey.Name.ShouldBe(name);
        await account.Users.Received(1).AddOrUpdatePasskeyAsync(user, passkey);
        account.StatusCookie.ShouldContain("Passkey updated successfully.");
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/Passkeys");
    }

    private static UserPasskeyInfo CreatePasskey() => new([251, 255, 0], [4, 5, 6], DateTimeOffset.UnixEpoch,
        0, ["internal"], true, false, false, [], [])
    { Name = "Laptop" };
}
