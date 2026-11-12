namespace YHAB.Features.Account.Models;

internal union SignInOutcome(SignInOutcome.Succeeded, SignInOutcome.RequiresTwoFactor, SignInOutcome.LockedOut, SignInOutcome.NotAllowed, SignInOutcome.Failed)
{
    internal readonly record struct Succeeded;
    internal readonly record struct RequiresTwoFactor;
    internal readonly record struct LockedOut;
    internal readonly record struct NotAllowed;
    internal readonly record struct Failed;
}
