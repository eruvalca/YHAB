using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Services;

namespace YHAB.Features.Account.Pages;

public sealed partial class ExternalLogin
{
    public const string LoginCallbackAction = "LoginCallback";

    private string? _message;
    private ExternalLoginInfo? _externalLoginInfo;
    private ExternalRegistrationOutcome.ExternalLoginLinkFailed? _linkFailure;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? RemoteError { get; set; }

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    private string? ProviderDisplayName => _externalLoginInfo?.ProviderDisplayName;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        if (RemoteError is not null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", $"Error from external provider: {RemoteError}", HttpContext);
            return;
        }

        var info = await SignInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information.", HttpContext);
            return;
        }

        _externalLoginInfo = info;

        if (HttpContext.Request.IsGet)
        {
            if (string.Equals(Action, LoginCallbackAction, StringComparison.Ordinal))
            {
                await OnLoginCallbackAsync();
                return;
            }

            // We should only reach this page via the login callback, so redirect back to
            // the login page if we get here some other way.
            RedirectManager.RedirectTo("Account/Login");
        }
    }

    private async Task OnLoginCallbackAsync()
    {
        if (_externalLoginInfo is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information.", HttpContext);
            return;
        }

        // Sign in the user with this external login provider if the user already has a login.
        var result = await AccountSignIn.ExternalAsync(
            _externalLoginInfo.LoginProvider,
            _externalLoginInfo.ProviderKey);

        _ = result switch
        {
            SignInOutcome.Succeeded => RedirectAfterSignIn(_externalLoginInfo),
            SignInOutcome.RequiresTwoFactor => RedirectToTwoFactor(),
            SignInOutcome.LockedOut => RedirectLockedOut(),
            SignInOutcome.NotAllowed => ReportInvalidAttempt(),
            SignInOutcome.Failed => PrefillEmail(_externalLoginInfo),
        };
    }

    private bool RedirectAfterSignIn(ExternalLoginInfo info)
    {
        LogUserLoggedInWithExternalProvider(Logger, info.Principal.Identity?.Name, info.LoginProvider);
        RedirectManager.RedirectTo(ReturnUrl);
        return true;
    }

    private bool RedirectToTwoFactor()
    {
        RedirectManager.RedirectTo("Account/LoginWith2fa", new(StringComparer.Ordinal) { ["returnUrl"] = ReturnUrl, ["rememberMe"] = false });
        return true;
    }

    private bool RedirectLockedOut()
    {
        RedirectManager.RedirectTo("Account/Lockout");
        return true;
    }

    private bool ReportInvalidAttempt()
    {
        RedirectManager.RedirectToWithStatus("Account/Login", "Error: Invalid login attempt.", HttpContext);
        return false;
    }

    private bool PrefillEmail(ExternalLoginInfo info)
    {
        Input.Email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? "";
        return true;
    }

    private async Task OnValidSubmitAsync()
    {
        if (_externalLoginInfo is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information during confirmation.", HttpContext);
            return;
        }

        var result = await AccountRegistration.ExternalAsync(Input.Email, _externalLoginInfo);
        await (result switch
        {
            RegistrationOutcome.Created created => CompleteRegistrationAsync(created.User, _externalLoginInfo.LoginProvider),
            RegistrationOutcome.CreationRejected rejected => ReportRejectedAsync(rejected),
            ExternalRegistrationOutcome.ExternalLoginLinkFailed failure => ReportLinkFailureAsync(failure),
        });
    }

    private Task ReportRejectedAsync(RegistrationOutcome.CreationRejected rejected)
    {
        _message = $"Error: {rejected.Errors.FormatDescriptions(",")}";
        return Task.CompletedTask;
    }

    private Task ReportLinkFailureAsync(ExternalRegistrationOutcome.ExternalLoginLinkFailed failure)
    {
        _linkFailure = failure;
        _message = $"Error: Your account was created, but the external login could not be linked. {failure.Errors.FormatDescriptions(",")}";
        return Task.CompletedTask;
    }

    private async Task CompleteRegistrationAsync(ApplicationUser user, string provider)
    {
        LogUserCreatedWithExternalProvider(Logger, provider);
        if (!UserManager.Options.SignIn.RequireConfirmedAccount)
        {
            await SignInManager.SignInAsync(user, isPersistent: false, provider);
            RedirectManager.RedirectTo(IdentityRedirectManager.RegistrationDestination(ReturnUrl));
            return;
        }

        var userId = await UserManager.GetUserIdAsync(user);
        var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
        code = code.EncodeIdentityToken();

        var callbackUrl = NavigationManager.GetUriWithQueryParameters(
            NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["userId"] = userId, ["code"] = code });
        await EmailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));

        RedirectManager.RedirectTo("Account/RegisterConfirmation", new(StringComparer.Ordinal) { ["email"] = Input.Email });
    }

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "{Name} logged in with {LoginProvider} provider.")]
    private static partial void LogUserLoggedInWithExternalProvider(ILogger logger, string? name, string loginProvider);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "User created an account using {Name} provider.")]
    private static partial void LogUserCreatedWithExternalProvider(ILogger logger, string name);

    private sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
    }
}
