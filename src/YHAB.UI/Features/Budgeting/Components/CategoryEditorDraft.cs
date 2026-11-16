using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

// Fluent renders unkeyed sortable wrappers, so a reorder recreates the row and its inline details
// editor. The table keeps that editor's draft here, keyed by the selected category, so a typed
// target, name, or note survives a reorder, a cross-group move, and a rejected move alike.
public sealed class CategoryEditorDraft
{
    public bool Initialized { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public Guid GroupId { get; set; }
    public bool GroupEdited { get; set; }
    public bool NameEdited { get; set; }
    public bool NotesEdited { get; set; }
    public bool HiddenEdited { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool HasTarget { get; set; }
    public TargetKind Kind { get; set; }
    public TargetCadence Cadence { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly StartMonth { get; set; }
    public int RepeatEveryMonths { get; set; }
    public DayOfWeek Weekday { get; set; }
    public Guid Replacement { get; set; }
    // Text the amount field holds before its editor commits it.
    public string AmountText { get; set; } = string.Empty;
}
