using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Pages;

public sealed partial class ResetPassword
{
    private IEnumerable<IdentityError>? _identityErrors;
    private bool _hasValidResetLink;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? Code { get; set; }

    private string? Message => _identityErrors is null ? null : $"Error: {_identityErrors.FormatDescriptions(", ")}";

    protected override void OnInitialized()
    {
        Input ??= new();

        TokenDecodeOutcome.Decode(Code).Switch(
            token =>
            {
                Input.Code = token.Value;
                _hasValidResetLink = true;
            },
            _ => RedirectManager.RedirectTo("Account/InvalidPasswordReset"));
    }

    private async Task OnValidSubmitAsync()
    {
        if (!_hasValidResetLink)
        {
            return;
        }
        var user = await UserManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Don't reveal that the user does not exist
            RedirectManager.RedirectTo("Account/ResetPasswordConfirmation");
            return;
        }

        var result = await UserManager.ResetPasswordAsync(user, Input.Code, Input.Password);
        if (result.Succeeded)
        {
            RedirectManager.RedirectTo("Account/ResetPasswordConfirmation");
            return;
        }

        _identityErrors = result.Errors;
    }

    private sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";

        [Required]
        public string Code { get; set; } = "";
    }
}
