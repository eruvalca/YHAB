using System.Diagnostics.CodeAnalysis;

namespace YHAB.UI;

/// <summary>
/// Identifies the assembly containing shared Razor components for route discovery.
/// </summary>
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty",
    Justification = "Identifies the shared UI assembly for route discovery through typeof(UiAssemblyMarker).Assembly.")]
public static class UiAssemblyMarker
{
}
