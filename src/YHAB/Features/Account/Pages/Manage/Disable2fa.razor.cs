using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class Disable2fa
{
    private ApplicationUser? _user;

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

        if (HttpContext.Request.IsGet && !await UserManager.GetTwoFactorEnabledAsync(_user))
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/TwoFactorAuthentication", "Two-factor authentication is already disabled.", HttpContext);
        }
    }

    private async Task OnSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var result = await AccountTwoFactor.DisableAsync(_user);
        await result.Match(
            async _ =>
            {
                var userId = await UserManager.GetUserIdAsync(_user);
                LogTwoFactorDisabled(Logger, userId);
                RedirectManager.RedirectToWithStatus(
                    "Account/Manage/TwoFactorAuthentication",
                    "2fa has been disabled. You can reenable 2fa when you setup an authenticator app", HttpContext);
            },
            _ => ShowStatusAsync("Account/Manage/TwoFactorAuthentication", "Two-factor authentication is already disabled."),
            _ => ShowStatusAsync("Account/Manage/Disable2fa", "Error: Two-factor authentication could not be disabled. Please try again."));
    }

    private Task ShowStatusAsync(string destination, string message)
    {
        RedirectManager.RedirectToWithStatus(destination, message, HttpContext);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1014, Level = LogLevel.Information, Message = "User with ID '{UserId}' has disabled 2fa.")]
    private static partial void LogTwoFactorDisabled(ILogger logger, string userId);
}
