using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
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

    [Fact]
    public void MeasurementKeepsNullPropertiesLikeThePersistenceSerializer()
    {
        var (view, _) = CreatePlan(categoryCount: 1, notesLength: 0);

        var json = JsonSerializer.Serialize(view, WorkspaceDisplayJsonContext.Default.PlanView);

        // Blazor persists with the web defaults, which write nulls. Omitting them
        // undercounts every category's CreditAccountId and Target.
        json.ShouldContain("\"creditAccountId\":null");
        json.ShouldContain("\"target\":null");
    }

    [Fact]
    public void NullPropertiesTipACategoryHeavyPlanPastTheBudget()
    {
        // Grow notes until the measured payload just crosses the budget.
        var notesLength = 0;
        PlanView view;
        BudgetMonth month;
        while (true)
        {
            (view, month) = CreatePlan(categoryCount: 20, notesLength);
            if (WorkspaceDisplayState.TransportBytes(Measure(view, month, omitNulls: false)) > Budget) { break; }
            notesLength += 10;
            notesLength.ShouldBeLessThan(5_000);
        }

        WorkspaceDisplayState.Fits(view, month).ShouldBeFalse();

        // The same payload without null-valued properties would still have fit, so the
        // null bytes are what tip it over.
        WorkspaceDisplayState.TransportBytes(Measure(view, month, omitNulls: true)).ShouldBeLessThanOrEqualTo(Budget);
    }

    private static (PlanView View, BudgetMonth Month) CreatePlan(int categoryCount, int notesLength)
    {
        var today = new DateOnly(2026, 10, 3);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        var notes = new string('n', notesLength);
        var categories = Enumerable.Range(0, categoryCount)
            .Select(index => new CategoryData(Guid.NewGuid(), group.Id, $"Category {index}", notes, index, false, null, null))
            .ToArray();
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [], [group], categories, [], [], false, false, []) { Today = today };
        return (new PlanView(plan, [], false), BudgetCalculator.Calculate(plan, BudgetFacts.Month(today), today));
    }

    private static int Measure(PlanView view, BudgetMonth month, bool omitNulls)
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            DefaultIgnoreCondition = omitNulls ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
        };
        return JsonSerializer.SerializeToUtf8Bytes(view, options).Length
            + JsonSerializer.SerializeToUtf8Bytes(month, options).Length;
    }
}
