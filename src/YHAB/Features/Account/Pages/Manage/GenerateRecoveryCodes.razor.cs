using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class GenerateRecoveryCodes
{
    private string? _message;
    private ApplicationUser? _user;
    private string[]? _recoveryCodes;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        _user = await UserManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var isTwoFactorEnabled = await UserManager.GetTwoFactorEnabledAsync(_user);
        if (!isTwoFactorEnabled)
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/TwoFactorAuthentication", "Error: Enable two-factor authentication before generating recovery codes.", HttpContext);
        }
    }

    private async Task OnSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        _recoveryCodes = null;
        var result = await AccountTwoFactor.GenerateRecoveryCodesAsync(_user);
        await result.Match(
            async generated =>
            {
                _recoveryCodes = generated.Codes;
                _message = "You have generated new recovery codes.";
                var userId = await UserManager.GetUserIdAsync(_user);
                LogRecoveryCodesGenerated(Logger, userId);
            },
            _ =>
            {
                RedirectManager.RedirectToWithStatus("Account/Manage/TwoFactorAuthentication", "Error: Enable two-factor authentication before generating recovery codes.", HttpContext);
                return Task.CompletedTask;
            },
            _ =>
            {
                _message = "Error: Recovery codes could not be generated. Please try again.";
                return Task.CompletedTask;
            });
    }

    [LoggerMessage(EventId = 1016, Level = LogLevel.Information, Message = "User with ID '{UserId}' has generated new 2FA recovery codes.")]
    private static partial void LogRecoveryCodesGenerated(ILogger logger, string userId);
}
