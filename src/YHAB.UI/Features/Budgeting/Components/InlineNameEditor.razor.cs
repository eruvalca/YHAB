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
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public bool Creating { get; set; }
    [Parameter] public bool Busy { get; set; }
    // Keeps the draft alive when a sortable reorder recreates this editor.
    [Parameter] public InlineNameDraft? Draft { get; set; }
    private FluentTextInput? _input;
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
