namespace YHAB.Features.Account.Services;

/// <summary>Cancellation for one Identity request or explicitly scoped revalidation.</summary>
internal sealed class IdentityCancellation
{
    public CancellationToken Token { get; set; }

    // Later Identity writes/cookie refresh must complete after the first irreversible write.
    public void CompleteWrite() => Token = CancellationToken.None;
}
