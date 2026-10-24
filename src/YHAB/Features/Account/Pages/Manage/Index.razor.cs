using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Services;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class Index
{
    [Inject] private IdentityCancellation Cancellation { get; set; } = default!;

    private ApplicationUser? _user;
    private string? _displayName;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = await UserManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        _displayName = UserDisplayNames.Normalize(_user.DisplayName);
        Input.DisplayName ??= UserDisplayNames.Resolve(_displayName, _user.Email, _user.UserName);
    }

    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var displayName = UserDisplayNames.Normalize(Input.DisplayName);
        if (displayName is not null && (UserDisplayNames.ContainsIdentity(displayName, _user.Email)
            || UserDisplayNames.ContainsIdentity(displayName, _user.UserName)))
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: Choose a display name that does not include your email address.", HttpContext);
            return;
        }

        if (!string.Equals(UserDisplayNames.Resolve(displayName, _user.Email, _user.UserName),
            UserDisplayNames.Resolve(_displayName, _user.Email, _user.UserName), StringComparison.Ordinal))
        {
            _user.DisplayName = displayName;
            var update = await UserManager.UpdateAsync(_user);
            if (!update.Succeeded)
            {
                RedirectManager.RedirectToCurrentPageWithStatus("Error: Failed to update your display name.", HttpContext);
                return;
            }
            Cancellation.CompleteWrite();
            _displayName = displayName;
        }

        await SignInManager.RefreshSignInAsync(_user);
        RedirectManager.RedirectToCurrentPageWithStatus("Your profile has been updated", HttpContext);
    }

    private sealed class InputModel
    {
        [StringLength(UserDisplayNames.MaximumLength)]
        [Display(Name = "Display name")]
        public string? DisplayName { get; set; }
    }
}
