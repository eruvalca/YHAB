namespace YHAB.Features.Account.Models;

internal union ResetAuthenticatorOutcome(ResetAuthenticatorOutcome.ResetCompleted, ResetAuthenticatorOutcome.DisableFailed, ResetAuthenticatorOutcome.DisabledButKeyResetFailed)
{
    internal readonly record struct ResetCompleted;
    internal readonly record struct DisableFailed;
    internal readonly record struct DisabledButKeyResetFailed;
}
