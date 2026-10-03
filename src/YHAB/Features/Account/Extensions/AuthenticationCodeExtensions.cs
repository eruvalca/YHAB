namespace YHAB.Features.Account.Extensions;

internal static class AuthenticationCodeExtensions
{
    extension(string code)
    {
        internal string NormalizeAuthenticatorCode() => code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        internal string NormalizeRecoveryCode() => code.Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
