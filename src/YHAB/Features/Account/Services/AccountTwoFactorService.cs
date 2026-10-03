using Microsoft.AspNetCore.Identity;
using YHAB.Data;
using YHAB.Features.Account.Extensions;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Services;

internal sealed class AccountTwoFactorService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
{
    private const int RecoveryCodeCount = 10;

    public async Task<AuthenticatorSetupOutcome> PrepareAsync(ApplicationUser user)
    {
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrEmpty(key))
        {
            return new AuthenticatorSetupOutcome.SetupReady(key);
        }
        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded)
        {
            return new AuthenticatorSetupOutcome.KeyInitializationFailed();
        }
        key = await userManager.GetAuthenticatorKeyAsync(user);
        return string.IsNullOrEmpty(key)
            ? new AuthenticatorSetupOutcome.KeyInitializationFailed()
            : new AuthenticatorSetupOutcome.SetupReady(key);
    }

    public async Task<EnableAuthenticatorOutcome> EnableAsync(ApplicationUser user, string code)
    {
        var normalizedCode = code.NormalizeAuthenticatorCode();
        if (!await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, normalizedCode))
        {
            return new EnableAuthenticatorOutcome.InvalidCode();
        }
        var enable = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enable.Succeeded)
        {
            return new EnableAuthenticatorOutcome.EnableFailed();
        }
        if (await userManager.CountRecoveryCodesAsync(user) != 0)
        {
            return new EnableAuthenticatorOutcome.Enabled();
        }
        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToArray();
        return codes is { Length: > 0 }
            ? new EnableAuthenticatorOutcome.EnabledWithRecoveryCodes(codes)
            : new EnableAuthenticatorOutcome.EnabledButRecoveryCodesFailed();
    }

    public async Task<ResetAuthenticatorOutcome> ResetAsync(ApplicationUser user)
    {
        var disable = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disable.Succeeded)
        {
            return new ResetAuthenticatorOutcome.DisableFailed();
        }
        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded)
        {
            return new ResetAuthenticatorOutcome.DisabledButKeyResetFailed();
        }
        await signInManager.RefreshSignInAsync(user);
        return new ResetAuthenticatorOutcome.ResetCompleted();
    }

    public async Task<DisableTwoFactorOutcome> DisableAsync(ApplicationUser user)
    {
        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return new DisableTwoFactorOutcome.AlreadyDisabled();
        }
        var result = await userManager.SetTwoFactorEnabledAsync(user, false);
        return result.Succeeded ? new DisableTwoFactorOutcome.Disabled() : new DisableTwoFactorOutcome.DisableFailed();
    }

    public async Task<RecoveryCodesOutcome> GenerateRecoveryCodesAsync(ApplicationUser user)
    {
        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return new RecoveryCodesOutcome.TwoFactorNotEnabled();
        }
        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToArray();
        return codes is { Length: > 0 }
            ? new RecoveryCodesOutcome.Generated(codes)
            : new RecoveryCodesOutcome.GenerationFailed();
    }
}
