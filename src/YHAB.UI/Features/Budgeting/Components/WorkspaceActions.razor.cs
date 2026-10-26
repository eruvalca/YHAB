using Microsoft.AspNetCore.Components;

namespace YHAB.UI.Features.Budgeting.Components;

// The plan-wide actions sit after each view's heading so assistive technology
// reaches the heading first.
public sealed partial class WorkspaceActions
{
    [Parameter] public bool CanUndo { get; set; }
    [Parameter] public bool CanRedo { get; set; }
    // Busy also covers a read-only or unreachable workspace; RefreshBusy is the
    // narrower in-flight read that the refresh action itself waits for.
    [Parameter] public bool Busy { get; set; }
    [Parameter] public bool RefreshBusy { get; set; }
    [Parameter, EditorRequired] public string Status { get; set; } = string.Empty;
    [Parameter, EditorRequired] public EventCallback OnUndo { get; set; }
    [Parameter, EditorRequired] public EventCallback OnRedo { get; set; }
    [Parameter, EditorRequired] public EventCallback OnRefresh { get; set; }
}
