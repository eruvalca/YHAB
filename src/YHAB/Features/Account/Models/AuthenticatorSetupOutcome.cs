namespace YHAB.Features.Account.Models;

internal union AuthenticatorSetupOutcome(AuthenticatorSetupOutcome.SetupReady, AuthenticatorSetupOutcome.KeyInitializationFailed)
{
    internal sealed record SetupReady(string Key);
    internal readonly record struct KeyInitializationFailed;
}
