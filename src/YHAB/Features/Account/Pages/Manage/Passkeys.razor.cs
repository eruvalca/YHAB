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

        await PasskeySubmission.From(Input).Match(
            _ => CompleteWithStatusAsync("Error: The browser did not provide a passkey."),
            async credential =>
            {
                var result = await AccountPasskeys.AddAsync(_user, credential.Json, _currentPasskeys!.Count);
                result.Switch(
                    added => RedirectManager.RedirectTo($"Account/Manage/RenamePasskey/{Base64Url.EncodeToString(added.CredentialId)}"),
                    _ => ShowStatus("Error: You have reached the maximum number of allowed passkeys."),
                    rejected => ShowStatus($"Error: Could not add the passkey: {rejected.Message}"),
                    _ => ShowStatus("Error: The passkey could not be added to your account."));
            },
            error => CompleteWithStatusAsync($"Error: {error.Message}"));
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

        await CredentialIdOutcome.Decode(CredentialId).Match(
            async decoded =>
            {
                var result = await UserManager.RemovePasskeyAsync(_user, decoded.Bytes);
                ShowStatus(result.Succeeded ? "Passkey deleted successfully." : "Error: The passkey could not be deleted.");
            },
            _ => CompleteWithStatusAsync("Error: The specified passkey ID had an invalid format."));
    }

    private void ShowStatus(string message) => RedirectManager.RedirectToCurrentPageWithStatus(message, HttpContext);

    private Task CompleteWithStatusAsync(string message)
    {
        ShowStatus(message);
        return Task.CompletedTask;
    }
}
