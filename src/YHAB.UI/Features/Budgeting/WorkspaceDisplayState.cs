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
    /// <summary>The default maximum size of a client message.</summary>
    internal const int CircuitStartLimitBytes = 32 * 1024;

    /// <summary>Headroom for component descriptors and other circuit-start fields.</summary>
    internal const int CircuitMetadataReserveBytes = 4 * 1024;

    // ASP.NET Core data protection adds an IV, a MAC, and up to one block of padding.
    private const int ProtectionOverheadBytes = 64;

    // Quotes, key names, and separators for the persisted property dictionary.
    private const int StateDictionaryOverheadBytes = 128;

    /// <summary>Drops the server-owned ledger and allocations the page does not render.</summary>
    public static PlanView Trim(PlanView view)
        => new(view.Catalog with { Transactions = [], Allocations = [] }, view.Balances, view.HasDueRecurring);

    /// <summary>
    /// Whether the display snapshot can travel with the circuit start. The catalog and
    /// the monthly projection both carry category data, so notes are counted twice.
    /// </summary>
    public static bool Fits(PlanView display, BudgetMonth? month)
    {
        var rawBytes = JsonSerializer.SerializeToUtf8Bytes(display, WorkspaceDisplayJsonContext.Default.PlanView).Length;
        if (month is not null)
        {
            rawBytes += JsonSerializer.SerializeToUtf8Bytes(month, WorkspaceDisplayJsonContext.Default.BudgetMonth).Length;
        }

        return TransportBytes(rawBytes) <= CircuitStartLimitBytes - CircuitMetadataReserveBytes;
    }

    /// <summary>
    /// The size the persisted state occupies in the circuit-start message: each value is
    /// data-protected and Base64-encoded, the state dictionary is serialized to JSON, and
    /// that JSON is Base64-encoded again before the client sends it back. Raw JSON is
    /// therefore a poor proxy; 20 KiB of it becomes roughly 35 KiB on the wire.
    /// </summary>
    internal static int TransportBytes(int rawJsonBytes)
    {
        var protectedBytes = rawJsonBytes + ProtectionOverheadBytes;
        var protectedBase64 = ((protectedBytes + 2) / 3) * 4;
        var dictionaryJson = protectedBase64 + StateDictionaryOverheadBytes;
        return ((dictionaryJson + 2) / 3) * 4;
    }
}
