namespace YHAB.Features.Account.Models;

internal union PasskeySubmission(PasskeySubmission.Missing, PasskeySubmission.Credential, PasskeySubmission.BrowserError)
{
    internal readonly record struct Missing;
    internal sealed record Credential(string Json);
    internal sealed record BrowserError(string Message);

    public static PasskeySubmission From(PasskeyInputModel? input)
    {
        if (!string.IsNullOrEmpty(input?.Error))
        {
            return new BrowserError(input.Error);
        }
        if (!string.IsNullOrEmpty(input?.CredentialJson))
        {
            return new Credential(input.CredentialJson);
        }
        return new Missing();
    }
}
