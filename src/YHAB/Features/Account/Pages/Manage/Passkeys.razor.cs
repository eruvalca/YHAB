using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Services;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class Passkeys
{
    private const int MaxPasskeyCount = AccountPasskeyService.MaxPasskeyCount;

    private ApplicationUser? _user;
    private IList<UserPasskeyInfo>? _currentPasskeys;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private string? Action { get; set; }

    [SupplyParameterFromForm]
    private string? CredentialId { get; set; }

    [SupplyParameterFromForm(FormName = "add-passkey")]
    private PasskeyInputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = await UserManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }
        _currentPasskeys = await UserManager.GetPasskeysAsync(_user);
    }

    private async Task AddPasskeyAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        await (PasskeySubmission.From(Input) switch
        {
            PasskeySubmission.Missing => CompleteWithStatusAsync("Error: The browser did not provide a passkey."),
            PasskeySubmission.Credential credential => SubmitPasskeyAsync(credential, _user),
            PasskeySubmission.BrowserError error => CompleteWithStatusAsync($"Error: {error.Message}"),
        });
    }

    private async Task SubmitPasskeyAsync(PasskeySubmission.Credential credential, ApplicationUser user)
    {
        var result = await AccountPasskeys.AddAsync(user, credential.Json, _currentPasskeys!.Count);
        await (result switch
        {
            AddPasskeyOutcome.Added added => RedirectToRenameAsync(added),
            AddPasskeyOutcome.LimitReached => CompleteWithStatusAsync("Error: You have reached the maximum number of allowed passkeys."),
            AddPasskeyOutcome.AttestationRejected rejected => CompleteWithStatusAsync($"Error: Could not add the passkey: {rejected.Message}"),
            AddPasskeyOutcome.PersistenceRejected => CompleteWithStatusAsync("Error: The passkey could not be added to your account."),
        });
    }

    private Task RedirectToRenameAsync(AddPasskeyOutcome.Added added)
    {
        RedirectManager.RedirectTo($"Account/Manage/RenamePasskey/{Base64Url.EncodeToString(added.CredentialId)}");
        return Task.CompletedTask;
    }

    private async Task UpdatePasskeyAsync()
    {
        switch (Action)
        {
            case "rename":
                RedirectManager.RedirectTo($"Account/Manage/RenamePasskey/{CredentialId}");
                break;
            case "delete":
                await DeletePasskeyAsync();
                break;
            default:
                RedirectManager.RedirectToCurrentPageWithStatus($"Error: Unknown action '{Action}'.", HttpContext);
                break;
        }
    }

    private async Task DeletePasskeyAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        await (CredentialIdOutcome.Decode(CredentialId) switch
        {
            CredentialIdOutcome.DecodedCredentialId decoded => RemovePasskeyAsync(decoded, _user),
            CredentialIdOutcome.InvalidCredentialId => CompleteWithStatusAsync("Error: The specified passkey ID had an invalid format."),
        });
    }

    private async Task RemovePasskeyAsync(CredentialIdOutcome.DecodedCredentialId decoded, ApplicationUser user)
    {
        var result = await UserManager.RemovePasskeyAsync(user, decoded.Bytes);
        ShowStatus(result.Succeeded ? "Passkey deleted successfully." : "Error: The passkey could not be deleted.");
    }

    private void ShowStatus(string message) => RedirectManager.RedirectToCurrentPageWithStatus(message, HttpContext);

    private Task CompleteWithStatusAsync(string message)
    {
        ShowStatus(message);
        return Task.CompletedTask;
    }
}
