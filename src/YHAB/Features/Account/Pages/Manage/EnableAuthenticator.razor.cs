using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class EnableAuthenticator
{
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded",
        Justification = "The otpauth URI format provisions TOTP authenticator apps; its scheme and structure are fixed protocol syntax.")]
    private static readonly CompositeFormat _authenticatorUriFormat =
        CompositeFormat.Parse("otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6");

    private string? _message;
    private ApplicationUser? _user;
    private string? _sharedKey;
    private string? _authenticatorUri;
    private string[]? _recoveryCodes;

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

        await LoadSharedKeyAndQrCodeUriAsync(_user);
    }

    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        if (_sharedKey is null)
        {
            return;
        }
        _recoveryCodes = null;
        var result = await AccountTwoFactor.EnableAsync(_user, Input.Code);
        await result.Match(
            _ => SetMessageAsync("Error: Verification code is invalid."),
            _ => CompleteEnableAsync(_user, null),
            enabled => CompleteEnableAsync(_user, enabled.Codes),
            _ => SetMessageAsync("Error: Two-factor authentication could not be enabled. Please try again."),
            _ => SetMessageAsync("Error: Two-factor authentication is enabled, but recovery codes could not be generated. Generate recovery codes from your two-factor authentication settings."));
    }

    private async Task CompleteEnableAsync(ApplicationUser user, string[]? codes)
    {
        var userId = await UserManager.GetUserIdAsync(user);
        LogAuthenticatorEnabled(Logger, userId);
        _message = "Your authenticator app has been verified.";
        _recoveryCodes = codes;
        if (codes is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/TwoFactorAuthentication", _message, HttpContext);
        }
    }

    private Task SetMessageAsync(string message)
    {
        _message = message;
        return Task.CompletedTask;
    }

    private async ValueTask LoadSharedKeyAndQrCodeUriAsync(ApplicationUser user)
    {
        var result = await AccountTwoFactor.PrepareAsync(user);
        await result.Match(
            async ready =>
            {
                _sharedKey = ready.Key.FormatAuthenticatorKey();
                var email = await UserManager.GetEmailAsync(user);
                _authenticatorUri = GenerateQrCodeUri(email!, ready.Key);
            },
            _ => SetMessageAsync("Error: The authenticator key could not be initialized. Please try again."));
    }

    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            _authenticatorUriFormat,
            UrlEncoder.Encode("Microsoft.AspNetCore.Identity.UI"),
            UrlEncoder.Encode(email),
            unformattedKey);
    }

    [LoggerMessage(EventId = 1015, Level = LogLevel.Information, Message = "User with ID '{UserId}' has enabled 2FA with an authenticator app.")]
    private static partial void LogAuthenticatorEnabled(ILogger logger, string userId);

    private sealed class InputModel
    {
        [Required]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Verification Code")]
        public string Code { get; set; } = "";
    }
}
