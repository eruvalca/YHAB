using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Services;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class ChangePassword
{
    [Inject] private IdentityCancellation Cancellation { get; set; } = default!;

    private string? _message;
    private ApplicationUser? _user;
    private bool _hasPassword;

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

        _hasPassword = await UserManager.HasPasswordAsync(_user);
        if (!_hasPassword)
        {
            RedirectManager.RedirectTo("Account/Manage/SetPassword");
        }
    }

    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var changePasswordResult = await UserManager.ChangePasswordAsync(_user, Input.OldPassword, Input.NewPassword);
        if (!changePasswordResult.Succeeded)
        {
            _message = $"Error: {changePasswordResult.Errors.FormatDescriptions(",")}";
            return;
        }

        Cancellation.CompleteWrite();
        await SignInManager.RefreshSignInAsync(_user);
        LogPasswordChanged(Logger);

        RedirectManager.RedirectToCurrentPageWithStatus("Your password has been changed", HttpContext);
    }

    [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "User changed their password successfully.")]
    private static partial void LogPasswordChanged(ILogger logger);

    private sealed class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string OldPassword { get; set; } = "";

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
