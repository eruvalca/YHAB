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
    private string? _username;
    private string? _phoneNumber;

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

        _username = await UserManager.GetUserNameAsync(_user);
        _phoneNumber = await UserManager.GetPhoneNumberAsync(_user);

        Input.PhoneNumber ??= _phoneNumber;
    }

    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        if (!string.Equals(Input.PhoneNumber, _phoneNumber, StringComparison.Ordinal))
        {
            var setPhoneResult = await UserManager.SetPhoneNumberAsync(_user, Input.PhoneNumber);
            if (!setPhoneResult.Succeeded)
            {
                RedirectManager.RedirectToCurrentPageWithStatus("Error: Failed to set phone number.", HttpContext);
                return;
            }
            Cancellation.CompleteWrite();
        }

        await SignInManager.RefreshSignInAsync(_user);
        RedirectManager.RedirectToCurrentPageWithStatus("Your profile has been updated", HttpContext);
    }

    private sealed class InputModel
    {
        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }
    }
}
