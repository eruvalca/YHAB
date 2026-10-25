using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Pages;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class ReportsTests
{
    [Theory]
    [InlineData("this-month", "2026-10-01", "2026-10-03")]
    [InlineData("last-month", "2026-09-01", "2026-09-30")]
    [InlineData("year-to-date", "2026-01-01", "2026-10-03")]
    public async Task PresetsUseThePlansTodayAndLoadThePreviousPeriodAsync(string preset, string from, string through)
    {
        await using var context = CreateContext(out var budgets, out var plan);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/plans/{plan.Id}/reports?preset={preset}");
        var component = context.Render<Reports>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await budgets.Received().ReadReportsAsync(plan.Id, DateOnly.Parse(from, CultureInfo.InvariantCulture),
            DateOnly.Parse(through, CultureInfo.InvariantCulture), Arg.Any<CancellationToken>());
        component.Find("#report-from").GetAttribute("value").ShouldBe(from);
        component.Find("#report-to").GetAttribute("value").ShouldBe(through);
    }

    [Fact]
    public async Task InvalidRangePreservesInputAndSkipsReportReadsAsync()
    {
        await using var context = CreateContext(out var budgets, out var plan);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/plans/{plan.Id}/reports?from=not-a-date&to=2026-01-01");
        var component = context.Render<Reports>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        component.Find("#report-from").GetAttribute("value").ShouldBe("not-a-date");
        component.Find("#report-to").GetAttribute("value").ShouldBe("2026-01-01");
        component.Find("#report-range-error").TextContent.ShouldContain("YYYY-MM-DD");
        await budgets.DidNotReceive().ReadReportsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        component.FindAll(".report-empty").ShouldBeEmpty();
    }

    [Fact]
    public void EmptyRangeOffersAWiderRange()
    {
        using var context = CreateContext(out _, out var plan);
        var component = context.Render<Reports>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        component.Find(".report-empty h2").TextContent.ShouldBe("No activity in this range");
        component.Find(".report-empty a").GetAttribute("href").ShouldBe($"/plans/{plan.Id}/reports?from=2025-10-03&to=2026-10-03");
    }

    [Fact]
    public void ActivityIncludesComparisonsChartsCsvAndCategoryDrillDown()
    {
        using var context = CreateContext(out var budgets, out var plan);
        var groceries = plan.Categories[0];
        budgets.ReadReportsAsync(plan.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var current = call.ArgAt<DateOnly>(1) == new DateOnly(2026, 10, 1);
            return new ReportView(plan.Version,
                [new(new DateOnly(2026, current ? 10 : 9, 1), current ? 120 : 100, current ? 40 : 30, 1000, 200)],
                current ? [new(groceries.Id, "Groceries, market", 40)] : []);
        });
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/plans/{plan.Id}/reports?from=2026-10-01&to=2026-10-03");
        var component = context.Render<Reports>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        component.Find(".summary-grid").TextContent.ShouldContain("+$20.00 vs previous period");
        component.Find(".trend-chart svg").GetAttribute("role").ShouldBe("img");
        component.Find(".spending-row").TextContent.ShouldContain("100.0% of spending");
        component.Find(".spending-row").TextContent.ShouldContain("Budgeted $75.00");
        component.Find(".spending-row a").GetAttribute("href").ShouldBe($"/plans/{plan.Id}/accounts?category={groceries.Id}&from=2026-10-01&to=2026-10-03");
        var export = component.Find(".report-actions a");
        export.GetAttribute("download").ShouldBe("reports-2026-10-01-2026-10-03.csv");
        var csv = Encoding.UTF8.GetString(Convert.FromBase64String(export.GetAttribute("href")!.Split(',')[1]));
        csv.ShouldContain("\"Month\",\"2026-10\",\"\",\"120.00\",\"40.00\",\"80.00\",\"1000.00\",\"200.00\",\"800.00\"");
        csv.ShouldContain("\"Category\",\"2026-10-01 to 2026-10-03\",\"Groceries, market\",\"\",\"40.00\"");
        csv.ShouldContain("\"75.00\",\"100.0%\"");
    }

    private static BunitContext CreateContext(out IBudgetClient budgets, out PlanSnapshot plan)
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        var category = new CategoryData(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null);
        plan = new(Guid.NewGuid(), "Plan", "", today, 7, [], [group], [category],
            [new(category.Id, new DateOnly(2026, 10, 1), 75)], [], false, false, [])
        { Today = today };
        budgets = Substitute.For<IBudgetClient>();
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(new PlanView(plan, [], false));
        budgets.ReadReportsAsync(plan.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new ReportView(plan.Version, [], []));
        context.Services.AddSingleton(budgets);
        return context;
    }
}
