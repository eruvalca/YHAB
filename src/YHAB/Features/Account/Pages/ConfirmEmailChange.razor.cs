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

        await TokenDecodeOutcome.Decode(Code).Match(
            async token =>
            {
                var result = await AccountEmailChange.ChangeAsync(user, Email, token.Value);
                _message = result.Match(
                    _ => "Thank you for confirming your email change.",
                    _ => "Error changing email.",
                    _ => "Error: Your email was changed, but your user name could not be updated.");
            },
            _ =>
            {
                RedirectManager.RedirectToWithStatus("Account/Login", "Error: Invalid email change confirmation link.", HttpContext);
                return Task.CompletedTask;
            });
    }
}
