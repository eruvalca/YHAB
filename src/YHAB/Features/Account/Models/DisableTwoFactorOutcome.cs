using OneOf;

namespace YHAB.Features.Account.Models;

[GenerateOneOf]
internal sealed partial class DisableTwoFactorOutcome : OneOfBase<DisableTwoFactorOutcome.Disabled, DisableTwoFactorOutcome.AlreadyDisabled, DisableTwoFactorOutcome.DisableFailed>
{
    internal readonly record struct Disabled;
    internal readonly record struct AlreadyDisabled;
    internal readonly record struct DisableFailed;
}
