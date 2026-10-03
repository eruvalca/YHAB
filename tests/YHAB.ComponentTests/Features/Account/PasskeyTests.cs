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
public sealed class PasskeyTests
{
    [Theory]
    [InlineData(null, null, "The browser did not provide a passkey.")]
    [InlineData("credential", "Browser refused", "Browser refused")]
    public async Task MissingOrBrowserRejectedSubmissionShowsStatusWithoutAttestingAsync(string? credential, string? browserError, string message)
    {
        await using var context = new BunitContext();
        var account = ConfigurePasskeys(context);
        var component = account.Render<Passkeys>(context);
        component.Instance.SetFormValue("Input", new PasskeyInputModel { CredentialJson = credential, Error = browserError });

        await component.Find("form").SubmitAsync();

        account.StatusCookie.ShouldContain(message);
        await account.SignIn.DidNotReceiveWithAnyArgs().PerformPasskeyAttestationAsync(default!);
    }

    [Fact]
    public async Task MaximumCountHidesAddButtonAndRejectsPostedCredentialAsync()
    {
        await using var context = new BunitContext();
        var account = ConfigurePasskeys(context, 100);
        var component = account.Render<Passkeys>(context);
        component.Instance.SetFormValue("Input", new PasskeyInputModel { CredentialJson = "credential" });

        component.FindAll("passkey-submit").ShouldBeEmpty();
        await component.FindAll("form")[100].SubmitAsync();

        account.StatusCookie.ShouldContain("maximum number of allowed passkeys");
        await account.SignIn.DidNotReceiveWithAnyArgs().PerformPasskeyAttestationAsync(default!);
    }

    [Fact]
    public async Task AddedPasskeyNavigatesToRenameWithEncodedCredentialAsync()
    {
        await using var context = new BunitContext();
        var account = ConfigurePasskeys(context);
        var passkey = CreatePasskey();
        account.SignIn.PerformPasskeyAttestationAsync("credential").Returns(PasskeyAttestationResult.Success(passkey,
            new PasskeyUserEntity { Id = "user", Name = "member", DisplayName = "Member" }));
        account.Users.AddOrUpdatePasskeyAsync(Arg.Any<Data.ApplicationUser>(), passkey).Returns(IdentityResult.Success);
        var component = account.Render<Passkeys>(context);
        component.Instance.SetFormValue("Input", new PasskeyInputModel { CredentialJson = "credential" });

        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/RenamePasskey/AQID");
        account.Http.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, "Could not add the passkey: Invalid origin")]
    [InlineData(true, "The passkey could not be added to your account.")]
    public async Task RejectedPasskeyAdditionShowsRelevantFailureAndDoesNotNavigateToRenameAsync(bool validAttestation, string message)
    {
        await using var context = new BunitContext();
        var account = ConfigurePasskeys(context);
        var passkey = CreatePasskey();
        account.SignIn.PerformPasskeyAttestationAsync("credential").Returns(validAttestation
            ? PasskeyAttestationResult.Success(passkey, new PasskeyUserEntity { Id = "user", Name = "member", DisplayName = "Member" })
            : PasskeyAttestationResult.Fail(new PasskeyException("Invalid origin")));
        account.Users.AddOrUpdatePasskeyAsync(Arg.Any<Data.ApplicationUser>(), passkey).Returns(IdentityResult.Failed());
        var component = account.Render<Passkeys>(context);
        component.Instance.SetFormValue("Input", new PasskeyInputModel { CredentialJson = "credential" });

        await component.Find("form").SubmitAsync();

        account.StatusCookie.ShouldContain(message);
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/Passkeys");
        if (!validAttestation)
        {
            await account.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid!")]
    public async Task InvalidDeleteIdShowsStatusWithoutRemovingPasskeyAsync(string? id)
    {
        await using var context = new BunitContext();
        var account = ConfigurePasskeys(context, 1);
        var component = account.Render<Passkeys>(context);
        component.Instance.SetFormValue("Action", "delete");
        component.Instance.SetFormValue("CredentialId", id);

        await component.FindAll("form")[0].SubmitAsync();

        account.StatusCookie.ShouldContain("specified passkey ID had an invalid format");
        await account.Users.DidNotReceiveWithAnyArgs().RemovePasskeyAsync(default!, default!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid!")]
    [InlineData("AQID")]
    public async Task InvalidOrMissingRenameTargetCannotSaveAfterInitializationRedirectAsync(string? id)
    {
        await using var context = new BunitContext();
        var account = ConfigurePasskeys(context);
        var component = context.Render<RenamePasskey>(parameters => parameters
            .AddCascadingValue<HttpContext>(account.Http).Add(page => page.Id, id));

        await component.Find("input[name='Input.Name']").ChangeAsync(new ChangeEventArgs { Value = "New name" });
        await component.Find("form").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/Passkeys");
        account.StatusCookie.ShouldContain(string.Equals(id, "AQID", StringComparison.Ordinal) ? "could not be found" : "invalid format");
        await account.Users.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!);
    }

    [Theory]
    [InlineData(false, "Error: The passkey could not be updated.")]
    [InlineData(true, "Passkey updated successfully.")]
    public async Task RenamePersistsSubmittedNameAndReportsSaveResultAsync(bool succeeds, string message)
    {
        await using var context = new BunitContext();
        var account = ConfigurePasskeys(context);
        var passkey = CreatePasskey();
        passkey.Name = "Old name";
        account.Users.GetPasskeyAsync(Arg.Any<Data.ApplicationUser>(), Arg.Any<byte[]>()).Returns(passkey);
        account.Users.AddOrUpdatePasskeyAsync(Arg.Any<Data.ApplicationUser>(), passkey)
            .Returns(succeeds ? IdentityResult.Success : IdentityResult.Failed());
        var component = context.Render<RenamePasskey>(parameters => parameters
            .AddCascadingValue<HttpContext>(account.Http).Add(page => page.Id, "AQID"));
        component.Find("h4").TextContent.ShouldContain("Old name");
        await component.Find("input[name='Input.Name']").ChangeAsync(new ChangeEventArgs { Value = "New name" });

        await component.Find("form").SubmitAsync();

        passkey.Name.ShouldBe("New name");
        await account.Users.Received(1).AddOrUpdatePasskeyAsync(Arg.Any<Data.ApplicationUser>(), passkey);
        account.StatusCookie.ShouldContain(message);
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/Manage/Passkeys");
    }

    private static AccountTestContext ConfigurePasskeys(BunitContext context, int count = 0)
    {
        var account = context.ConfigureAccount();
        var user = account.Authenticate();
        account.Users.GetPasskeysAsync(user).Returns(Enumerable.Range(0, count).Select(_ => CreatePasskey()).ToList());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Account/Manage/Passkeys");
        return account;
    }

    private static UserPasskeyInfo CreatePasskey() => new([1, 2, 3], [4, 5, 6], DateTimeOffset.UnixEpoch,
        0, ["internal"], true, false, false, [], []);
}
