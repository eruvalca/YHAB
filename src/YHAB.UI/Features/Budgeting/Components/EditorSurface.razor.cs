using Microsoft.AspNetCore.Components;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class EditorSurface
{
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    private Task CloseAsync() => OnClose.InvokeAsync();
}
