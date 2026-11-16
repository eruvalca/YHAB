using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

// A row's request to show a category in the details pane. Inline controls that keep their own focus
// target, such as the name editor and the Assigned field, select without moving focus into the editor.
public sealed record CategorySelection(CategoryData Category, bool FocusEditor = true);
