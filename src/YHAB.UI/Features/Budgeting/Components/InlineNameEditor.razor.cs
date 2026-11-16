using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class InlineNameEditor
{
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;
    [Parameter] public string Value { get; set; } = string.Empty;
    [Parameter, EditorRequired] public EventCallback<string> OnSave { get; set; }
    [Parameter] public EventCallback OnSelect { get; set; }
    // A category row selects itself here without moving focus, because the inline field keeps it.
    [Parameter] public EventCallback OnBeginRename { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public bool Creating { get; set; }
    [Parameter] public bool Busy { get; set; }
    // Keeps the draft alive when a sortable reorder recreates this editor.
    [Parameter] public InlineNameDraft? Draft { get; set; }
    /// <summary>
    /// The expanded state a disclosure-style name reports, or null when the
    /// component's name is not a disclosure target.
    /// </summary>
    [Parameter] public string? AriaExpanded { get; set; }
    [Parameter] public string? AriaControls { get; set; }
    /// <summary>
    /// A closing editor returned focus to this name; the component focuses it and
    /// lets the caller clear the request.
    /// </summary>
    [Parameter] public bool FocusName { get; set; }
    [Parameter] public EventCallback OnNameFocused { get; set; }
    private FluentTextInput? _input;
    private ElementReference _nameControl;
    private string _name = string.Empty;
    private string? _previousValue;
    private bool _editing;
    private bool _focus;

    private string Name
    {
        get => _name;
        set
        {
            if (string.Equals(_name, value, StringComparison.Ordinal)) { return; }
            _name = value;
            Persist();
        }
    }

    protected override void OnParametersSet()
    {
        if (Draft is { } draft)
        {
            if (!string.Equals(draft.SourceValue, Value, StringComparison.Ordinal))
            {
                draft.SourceValue = Value;
                draft.Name = Value;
                draft.Editing = false;
            }
            _previousValue = draft.SourceValue;
            _name = draft.Name;
            _editing = draft.Editing;
            return;
        }
        if (!string.Equals(_previousValue, Value, StringComparison.Ordinal))
        {
            _previousValue = Value;
            _name = Value;
            _editing = false;
        }
    }

    private void Persist()
    {
        if (Draft is not { } draft) { return; }
        draft.SourceValue = _previousValue;
        draft.Name = _name;
        draft.Editing = _editing;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (FocusName)
        {
            // A batch can replace the name with its rename form, so the request
            // clears even when there is no rendered name to focus.
            if (_nameControl.Id is not null) { await _nameControl.FocusAsync(); }
            await OnNameFocused.InvokeAsync();
        }
        if ((_focus || (firstRender && Creating)) && _input is not null)
        {
            _focus = false;
            await _input.Element.FocusAsync();
        }
    }

    private void BeginEdit()
    {
        if (Busy) { return; }
        _name = Value;
        _editing = true;
        _focus = true;
        Persist();
    }
    private void NameKeyDown(KeyboardEventArgs args) { if (string.Equals(args.Key, "F2", StringComparison.Ordinal)) { BeginEdit(); } }
    private Task SelectAsync() { if (OnSelect.HasDelegate) { return OnSelect.InvokeAsync(); } BeginEdit(); return Task.CompletedTask; }
    // Renaming is a way of choosing the category, so the row selects before the editor opens.
    private async Task RenameAsync()
    {
        if (Busy) { return; }
        if (OnBeginRename.HasDelegate) { await OnBeginRename.InvokeAsync(); }
        BeginEdit();
    }
    private Task SaveAsync()
    {
        if (Busy || string.IsNullOrWhiteSpace(_name)) { return Task.CompletedTask; }
        return !Creating && string.Equals(_name.Trim(), Value, StringComparison.Ordinal) ? CancelAsync() : OnSave.InvokeAsync(_name.Trim());
    }
    private Task KeyDownAsync(KeyboardEventArgs args) => string.Equals(args.Key, "Escape", StringComparison.Ordinal) ? CancelAsync() : Task.CompletedTask;
    private Task CancelAsync()
    {
        _editing = false;
        _name = Value;
        Persist();
        return OnCancel.InvokeAsync();
    }
}
