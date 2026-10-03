using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;

namespace YHAB.Features.Account.Pages;

public sealed partial class LoginWithRecoveryCode
{
    private string? _message;
    private ApplicationUser _user = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        // Ensure the user has gone through the username & password screen first
        _user = await SignInManager.GetTwoFactorAuthenticationUserAsync() ??
            throw new InvalidOperationException("Unable to load two-factor authentication user.");
    }

    private async Task OnValidSubmitAsync()
    {
        var result = await AccountSignIn.RecoveryCodeAsync(Input.RecoveryCode);

        var userId = await UserManager.GetUserIdAsync(_user);

        result.Switch(
            _ =>
            {
                LogUserLoggedInWithRecoveryCode(Logger, userId);
                RedirectManager.RedirectTo(ReturnUrl);
            },
            _ => ShowInvalidCode(userId),
            _ =>
            {
                LogUserLockedOut(Logger);
                RedirectManager.RedirectTo("Account/Lockout");
            },
            _ => ShowInvalidCode(userId),
            _ => ShowInvalidCode(userId));
    }

    private void ShowInvalidCode(string userId)
    {
        LogInvalidRecoveryCode(Logger, userId);
        _message = "Error: Invalid recovery code entered.";
    }

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "User with ID '{UserId}' logged in with a recovery code.")]
    private static partial void LogUserLoggedInWithRecoveryCode(ILogger logger, string userId);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "User account locked out.")]
    private static partial void LogUserLockedOut(ILogger logger);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "Invalid recovery code entered for user with ID '{UserId}' ")]
    private static partial void LogInvalidRecoveryCode(ILogger logger, string userId);

    private sealed class InputModel
    {
        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "Recovery Code")]
        public string RecoveryCode { get; set; } = "";
    }
}
