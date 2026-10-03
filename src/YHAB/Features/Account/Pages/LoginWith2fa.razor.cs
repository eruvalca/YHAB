using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;

namespace YHAB.Features.Account.Pages;

public sealed partial class LoginWith2fa
{
    private string? _message;
    private ApplicationUser _user = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery]
    private bool RememberMe { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        // Ensure the user has gone through the username & password screen first
        _user = await SignInManager.GetTwoFactorAuthenticationUserAsync() ??
            throw new InvalidOperationException("Unable to load two-factor authentication user.");
    }

    private async Task OnValidSubmitAsync()
    {
        var result = await AccountSignIn.AuthenticatorAsync(Input.TwoFactorCode!, RememberMe, Input.RememberMachine);
        var userId = await UserManager.GetUserIdAsync(_user);

        result.Switch(
            _ =>
            {
                LogUserLoggedInWithTwoFactor(Logger, userId);
                RedirectManager.RedirectTo(ReturnUrl);
            },
            _ => ShowInvalidCode(userId),
            _ =>
            {
                LogUserLockedOut(Logger, userId);
                RedirectManager.RedirectTo("Account/Lockout");
            },
            _ => ShowInvalidCode(userId),
            _ => ShowInvalidCode(userId));
    }

    private void ShowInvalidCode(string userId)
    {
        LogInvalidAuthenticatorCode(Logger, userId);
        _message = "Error: Invalid authenticator code.";
    }

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "User with ID '{UserId}' logged in with 2fa.")]
    private static partial void LogUserLoggedInWithTwoFactor(ILogger logger, string userId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "User with ID '{UserId}' account locked out.")]
    private static partial void LogUserLockedOut(ILogger logger, string userId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Invalid authenticator code entered for user with ID '{UserId}'.")]
    private static partial void LogInvalidAuthenticatorCode(ILogger logger, string userId);

    private sealed class InputModel
    {
        [Required]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Authenticator code")]
        public string? TwoFactorCode { get; set; }

        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }
}
