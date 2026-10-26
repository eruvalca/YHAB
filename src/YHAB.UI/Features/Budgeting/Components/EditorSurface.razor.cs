using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class EditorSurface
{
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    // Evaluated on demand: the keyboard path can ask before the change that made
    // the draft dirty has re-rendered this parameter.
    [Parameter] public Func<bool>? HasUnsavedChanges { get; set; }
    private readonly string _discardId = $"editor-discard-{Guid.CreateVersion7():N}";
    private ElementReference _heading;
    private ElementReference _discardPrompt;
    private bool _confirmingDiscard;
    private bool _focusDiscard;
    private bool IsDirty => HasUnsavedChanges?.Invoke() == true;

    protected override void OnParametersSet()
    {
        // A pending focus belongs to the render that showed the prompt; a later
        // parameter update clears the prompt, and only then may it be dropped.
        if (!IsDirty && !_focusDiscard)
        {
            _confirmingDiscard = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await _heading.FocusAsync();
        }
        else if (_focusDiscard)
        {
            _focusDiscard = false;
            // A batch can clear the prompt before its reference was ever captured. Focusing that
            // unconfigured reference throws and takes the circuit down with it, so the prompt only
            // takes focus when this instance actually rendered it.
            if (_confirmingDiscard && _discardPrompt.Id is not null)
            {
                await _discardPrompt.FocusAsync();
            }
        }
    }

    internal async Task RequestCloseAsync()
    {
        if (!IsDirty)
        {
            await OnClose.InvokeAsync();
            return;
        }

        // A close request never discards: only the explicit Discard changes button does.
        if (!_confirmingDiscard)
        {
            _confirmingDiscard = true;
            _focusDiscard = true;
        }
    }

    private Task DiscardAsync() => OnClose.InvokeAsync();

    private async Task KeepEditingAsync()
    {
        _confirmingDiscard = false;
        await _heading.FocusAsync();
    }

    private async Task KeyDownAsync(KeyboardEventArgs args)
    {
        if (!string.Equals(args.Key, "Escape", StringComparison.Ordinal) || args.IsComposing)
        {
            return;
        }

        await RequestCloseAsync();
    }
}
