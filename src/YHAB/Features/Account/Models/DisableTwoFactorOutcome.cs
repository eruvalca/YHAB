namespace YHAB.Features.Account.Models;

internal union DisableTwoFactorOutcome(DisableTwoFactorOutcome.Disabled, DisableTwoFactorOutcome.AlreadyDisabled, DisableTwoFactorOutcome.DisableFailed)
{
    internal readonly record struct Disabled;
    internal readonly record struct AlreadyDisabled;
    internal readonly record struct DisableFailed;
}
