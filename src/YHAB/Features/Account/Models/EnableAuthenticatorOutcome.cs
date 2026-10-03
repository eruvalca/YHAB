using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class EnableAuthenticatorOutcome : OneOfBase<EnableAuthenticatorOutcome.InvalidCode, EnableAuthenticatorOutcome.Enabled, EnableAuthenticatorOutcome.EnabledWithRecoveryCodes, EnableAuthenticatorOutcome.EnableFailed, EnableAuthenticatorOutcome.EnabledButRecoveryCodesFailed>
{
    internal readonly record struct InvalidCode;
    internal readonly record struct Enabled;
    internal sealed record EnabledWithRecoveryCodes(string[] Codes);
    internal readonly record struct EnableFailed;
    internal readonly record struct EnabledButRecoveryCodesFailed;
}
