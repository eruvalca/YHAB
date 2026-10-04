using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Services;

internal sealed class AccountEmailChangeService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IdentityCancellation cancellation)
{
    public async Task<EmailChangeOutcome> ChangeAsync(ApplicationUser user, string email, string token)
    {
        cancellation.Token.ThrowIfCancellationRequested();
        var emailChange = await userManager.ChangeEmailAsync(user, email, token);
        if (!emailChange.Succeeded)
        {
            return new EmailChangeOutcome.EmailChangeRejected(emailChange.Errors.ToArray());
        }
        cancellation.CompleteWrite();
        var usernameChange = await userManager.SetUserNameAsync(user, email);
        if (!usernameChange.Succeeded)
        {
            return new EmailChangeOutcome.UsernameChangeRejected(usernameChange.Errors.ToArray());
        }
        await signInManager.RefreshSignInAsync(user);
        return new EmailChangeOutcome.Changed();
    }
}
