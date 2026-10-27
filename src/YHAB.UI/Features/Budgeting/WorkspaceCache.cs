using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting;

/// <summary>
/// The last displayed catalog and month for recently opened plans. The service is
/// scoped to the interactive renderer, so entries never cross a user or circuit.
/// It lets a workspace remounted by enhanced navigation paint its previous state
/// while the fresh read is in flight.
/// </summary>
public sealed class WorkspaceCache
{
    private const int Capacity = 4;
    private readonly LinkedList<Entry> _entries = new();

    public bool TryGet(Guid planId, out PlanView view, out BudgetMonth? month)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            if (node.Value.PlanId != planId) { continue; }
            view = node.Value.View;
            month = node.Value.Month;
            _entries.Remove(node);
            _entries.AddFirst(node);
            return true;
        }

        view = null!;
        month = null;
        return false;
    }

    public void Store(Guid planId, PlanView view, BudgetMonth? month)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            if (node.Value.PlanId != planId) { continue; }
            node.Value = new(planId, view, month);
            _entries.Remove(node);
            _entries.AddFirst(node);
            return;
        }

        _entries.AddFirst(new Entry(planId, view, month));
        while (_entries.Count > Capacity) { _entries.RemoveLast(); }
    }

    public void Remove(Guid planId)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            if (node.Value.PlanId != planId) { continue; }
            _entries.Remove(node);
            return;
        }
    }

    private sealed record Entry(Guid PlanId, PlanView View, BudgetMonth? Month);
}
