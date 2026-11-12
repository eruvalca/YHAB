using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Pages;

public sealed partial class ConfirmEmailChange
{
    private string? _message;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? UserId { get; set; }

    [SupplyParameterFromQuery]
    private string? Email { get; set; }

    [SupplyParameterFromQuery]
    private string? Code { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (UserId is null || Email is null || Code is null)
        {
            RedirectManager.RedirectToWithStatus(
                "Account/Login", "Error: Invalid email change confirmation link.", HttpContext);
            return;
        }

        var user = await UserManager.FindByIdAsync(UserId);
        if (user is null)
        {
            _message = $"Unable to find user with Id '{UserId}'";
            return;
        }

        await (TokenDecodeOutcome.Decode(Code) switch
        {
            TokenDecodeOutcome.DecodedToken token => ConfirmAsync(user, Email, token),
            TokenDecodeOutcome.InvalidToken => ReportInvalidAsync(),
        });
    }

    private async Task ConfirmAsync(ApplicationUser user, string email, TokenDecodeOutcome.DecodedToken token)
    {
        var result = await AccountEmailChange.ChangeAsync(user, email, token.Value);
        _message = result switch
        {
            EmailChangeOutcome.Changed => "Thank you for confirming your email change.",
            EmailChangeOutcome.EmailChangeRejected => "Error changing email.",
            EmailChangeOutcome.UsernameChangeRejected => "Error: Your email was changed, but your user name could not be updated.",
        };
    }

    private Task ReportInvalidAsync()
    {
        RedirectManager.RedirectToWithStatus("Account/Login", "Error: Invalid email change confirmation link.", HttpContext);
        return Task.CompletedTask;
    }
}
