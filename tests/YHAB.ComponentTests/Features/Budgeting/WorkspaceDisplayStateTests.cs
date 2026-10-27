using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.UI.Features.Budgeting;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class WorkspaceDisplayStateTests
{
    private static int Budget => WorkspaceDisplayState.CircuitStartLimitBytes - WorkspaceDisplayState.CircuitMetadataReserveBytes;

    [Fact]
    public void RawPayloadAtTheOldCapExceedsTheTransportBudget()
    {
        // 20 KiB of raw JSON becomes roughly 35 KiB after protection and double Base64,
        // so a raw-JSON cap at that level can still fail the circuit start.
        WorkspaceDisplayState.TransportBytes(20 * 1024).ShouldBeGreaterThan(Budget);
    }

    [Fact]
    public void AcceptedPayloadStaysInsideTheTransportBudget()
    {
        WorkspaceDisplayState.TransportBytes(15 * 1024).ShouldBeLessThanOrEqualTo(Budget);
    }
}
