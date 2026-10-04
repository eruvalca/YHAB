using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Services;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class SetPassword
{
    [Inject] private IdentityCancellation Cancellation { get; set; } = default!;

    private string? _message;
    private ApplicationUser? _user;

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

        var hasPassword = await UserManager.HasPasswordAsync(_user);
        if (hasPassword)
        {
            RedirectManager.RedirectTo("Account/Manage/ChangePassword");
        }
    }

    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var addPasswordResult = await UserManager.AddPasswordAsync(_user, Input.NewPassword!);
        if (!addPasswordResult.Succeeded)
        {
            _message = $"Error: {addPasswordResult.Errors.FormatDescriptions(",")}";
            return;
        }

        Cancellation.CompleteWrite();
        await SignInManager.RefreshSignInAsync(_user);
        RedirectManager.RedirectToCurrentPageWithStatus("Your password has been set.", HttpContext);
    }

    private sealed class InputModel
    {
        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}
