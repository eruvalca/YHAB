using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class PlanFrame
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;
    [Parameter] public IReadOnlyList<AccountBalance>? Balances { get; set; }
    [Parameter] public EventCallback OnAddAccount { get; set; }
    [Parameter] public bool Busy { get; set; }
}
