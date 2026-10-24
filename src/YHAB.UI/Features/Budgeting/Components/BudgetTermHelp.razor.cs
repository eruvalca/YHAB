using Microsoft.AspNetCore.Components;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetTermHelp
{
    [Parameter, EditorRequired] public BudgetTerm Term { get; set; }
    [Parameter] public EventCallback OnExplain { get; set; }

    private bool _open;
    private string Label => BudgetGlossary.Label(Term);
    private string Definition => BudgetGlossary.Definition(Term);
    private string Id => $"budget-term-{Term}";

    private void Toggle() => _open = !_open;

    private Task ExplainAsync()
    {
        _open = false;
        return OnExplain.InvokeAsync();
    }
}
