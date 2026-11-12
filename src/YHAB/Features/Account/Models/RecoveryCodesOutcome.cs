namespace YHAB.Features.Account.Models;

internal union RecoveryCodesOutcome(RecoveryCodesOutcome.Generated, RecoveryCodesOutcome.TwoFactorNotEnabled, RecoveryCodesOutcome.GenerationFailed)
{
    internal sealed record Generated(string[] Codes);
    internal readonly record struct TwoFactorNotEnabled;
    internal readonly record struct GenerationFailed;
}
