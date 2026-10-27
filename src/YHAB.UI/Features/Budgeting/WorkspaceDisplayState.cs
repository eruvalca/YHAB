using System.Text.Json;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting;

/// <summary>
/// The display-only snapshot handed from prerendering to the interactive renderer.
/// Prerendered state travels inside the circuit-start message, whose default SignalR
/// receive limit is 32 KiB, so a snapshot that does not fit is not persisted at all.
/// </summary>
internal static class WorkspaceDisplayState
{
    // Leaves headroom in the 32 KiB circuit-start message for the rest of the payload.
    internal const int MaximumPersistedBytes = 20 * 1024;

    /// <summary>Drops the server-owned ledger and allocations the page does not render.</summary>
    public static PlanView Trim(PlanView view)
        => new(view.Catalog with { Transactions = [], Allocations = [] }, view.Balances, view.HasDueRecurring);

    /// <summary>
    /// Whether the display snapshot can travel with the circuit start. The catalog and
    /// the monthly projection both carry category data, so notes are counted twice.
    /// </summary>
    public static bool Fits(PlanView display, BudgetMonth? month)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(display, WorkspaceDisplayJsonContext.Default.PlanView).Length;
        if (month is not null)
        {
            bytes += JsonSerializer.SerializeToUtf8Bytes(month, WorkspaceDisplayJsonContext.Default.BudgetMonth).Length;
        }

        return bytes <= MaximumPersistedBytes;
    }
}
