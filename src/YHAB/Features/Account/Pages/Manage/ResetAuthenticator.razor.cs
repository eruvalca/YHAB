using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class ResetAuthenticator
{
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    private async Task OnSubmitAsync()
    {
        var user = await UserManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var result = await AccountTwoFactor.ResetAsync(user);
        await (result switch
        {
            ResetAuthenticatorOutcome.ResetCompleted => OnResetAsync(user),
            ResetAuthenticatorOutcome.DisableFailed => ShowFailureAsync("Error: Two-factor authentication could not be disabled. Your authenticator key was not reset."),
            ResetAuthenticatorOutcome.DisabledButKeyResetFailed => ShowFailureAsync("Error: Two-factor authentication was disabled, but your authenticator key could not be reset. Retry resetting your authenticator before enabling two-factor authentication again."),
        });
    }

    private async Task OnResetAsync(ApplicationUser user)
    {
        var userId = await UserManager.GetUserIdAsync(user);
        LogAuthenticatorReset(Logger, userId);
        RedirectManager.RedirectToWithStatus(
            "Account/Manage/EnableAuthenticator",
            "Your authenticator app key has been reset, you will need to configure your authenticator app using the new key.", HttpContext);
    }

    private Task ShowFailureAsync(string message)
    {
        RedirectManager.RedirectToCurrentPageWithStatus(message, HttpContext);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1017, Level = LogLevel.Information, Message = "User with ID '{UserId}' has reset their authentication app key.")]
    private static partial void LogAuthenticatorReset(ILogger logger, string userId);
}
