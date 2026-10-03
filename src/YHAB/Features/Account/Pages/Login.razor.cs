using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Pages;

public sealed partial class Login
{
    private string? _errorMessage;
    private EditContext _editContext = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private LoginInputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _editContext = new EditContext(Input);

        if (HttpContext.Request.IsGet)
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    public Task LoginUserAsync() => PasskeySubmission.From(Input.Passkey).Match(
        _ => PasswordLoginAsync(),
        async credential => HandleSignIn(await AccountSignIn.PasskeyAsync(credential.Json)),
        error =>
        {
            _errorMessage = $"Error: {error.Message}";
            return Task.CompletedTask;
        });

    private async Task PasswordLoginAsync()
    {
        if (!_editContext.Validate())
        {
            return;
        }
        HandleSignIn(await AccountSignIn.PasswordAsync(Input.Email, Input.Password, Input.RememberMe));
    }

    private void HandleSignIn(SignInOutcome result)
    {
        result.Switch(
            _ =>
            {
                LogUserLoggedIn(Logger);
                RedirectManager.RedirectTo(ReturnUrl);
            },
            _ => RedirectManager.RedirectTo(
                "Account/LoginWith2fa",
                new(StringComparer.Ordinal) { ["returnUrl"] = ReturnUrl, ["rememberMe"] = Input.RememberMe }),
            _ =>
            {
                LogUserLockedOut(Logger);
                RedirectManager.RedirectTo("Account/Lockout");
            },
            _ => _errorMessage = "Error: Invalid login attempt.",
            _ => _errorMessage = "Error: Invalid login attempt.");
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "User logged in.")]
    private static partial void LogUserLoggedIn(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "User account locked out.")]
    private static partial void LogUserLockedOut(ILogger logger);
}
