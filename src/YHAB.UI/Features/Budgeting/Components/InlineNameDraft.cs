namespace YHAB.UI.Features.Budgeting.Components;

// Fluent renders unkeyed sortable wrappers, so a reorder recreates the row and its inline editor.
// The table keeps the open editor state here, keyed by category, so an unsaved name survives the reorder.
public sealed class InlineNameDraft
{
    public string? SourceValue { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Editing { get; set; }
}
