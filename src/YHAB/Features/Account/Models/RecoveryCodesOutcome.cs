using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class RecoveryCodesOutcome : OneOfBase<RecoveryCodesOutcome.Generated, RecoveryCodesOutcome.TwoFactorNotEnabled, RecoveryCodesOutcome.GenerationFailed>
{
    internal sealed record Generated(string[] Codes);
    internal readonly record struct TwoFactorNotEnabled;
    internal readonly record struct GenerationFailed;
}
