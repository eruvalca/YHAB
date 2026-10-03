using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class RenamePasskey
{
    private ApplicationUser? _user;
    private UserPasskeyInfo? _passkey;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [Parameter]
    public string? Id { get; set; }

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = (await UserManager.GetUserAsync(HttpContext.User))!;
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var result = await AccountPasskeys.FindAsync(_user, Id);
        result.Switch(
            found => _passkey = found.Passkey,
            _ => RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The specified passkey ID had an invalid format.", HttpContext),
            _ => RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The specified passkey could not be found.", HttpContext));
    }

    private async Task RenameAsync()
    {
        // Static SSR navigation does not throw; a POST can still dispatch after initialization redirects.
        if (_user is null || _passkey is null)
        {
            return;
        }
        _passkey.Name = Input.Name;
        var result = await UserManager.AddOrUpdatePasskeyAsync(_user, _passkey);
        if (!result.Succeeded)
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The passkey could not be updated.", HttpContext);
            return;
        }

        RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Passkey updated successfully.", HttpContext);
    }

    private sealed class InputModel
    {
        [Required]
        [StringLength(200, ErrorMessage = "Passkey names must be no longer than {1} characters.")]
        public string Name { get; set; } = "";
    }
}
