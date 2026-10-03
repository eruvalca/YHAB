using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Services;

internal sealed class AccountRegistrationService(UserManager<ApplicationUser> userManager, IUserStore<ApplicationUser> userStore)
{
    public async Task<RegistrationOutcome> PasswordAsync(string email, string password)
    {
        var user = await InitializeUserAsync(email);
        var result = await userManager.CreateAsync(user, password);
        return result.Succeeded
            ? new RegistrationOutcome.Created(user)
            : new RegistrationOutcome.CreationRejected(result.Errors.ToArray());
    }

    public async Task<ExternalRegistrationOutcome> ExternalAsync(string email, ExternalLoginInfo login)
    {
        var user = await InitializeUserAsync(email);
        var creation = await userManager.CreateAsync(user);
        if (!creation.Succeeded)
        {
            return new RegistrationOutcome.CreationRejected(creation.Errors.ToArray());
        }
        var linking = await userManager.AddLoginAsync(user, login);
        return linking.Succeeded
            ? new RegistrationOutcome.Created(user)
            : new ExternalRegistrationOutcome.ExternalLoginLinkFailed(user, linking.Errors.ToArray());
    }

    private async Task<ApplicationUser> InitializeUserAsync(string email)
    {
        if (!userManager.SupportsUserEmail)
        {
            throw new NotSupportedException("The default UI requires a user store with email support.");
        }
        var user = new ApplicationUser();
        await userStore.SetUserNameAsync(user, email, CancellationToken.None);
        await ((IUserEmailStore<ApplicationUser>)userStore).SetEmailAsync(user, email, CancellationToken.None);
        return user;
    }
}
