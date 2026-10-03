using Microsoft.AspNetCore.Components;

namespace YHAB.Features.Account.Components;

public sealed partial class ShowRecoveryCodes
{
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<string> RecoveryCodes { get; set; } = [];

    [Parameter]
    public string? StatusMessage { get; set; }
}
