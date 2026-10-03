using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class AuthenticatorSetupOutcome : OneOfBase<AuthenticatorSetupOutcome.SetupReady, AuthenticatorSetupOutcome.KeyInitializationFailed>
{
    internal sealed record SetupReady(string Key);
    internal readonly record struct KeyInitializationFailed;
}
