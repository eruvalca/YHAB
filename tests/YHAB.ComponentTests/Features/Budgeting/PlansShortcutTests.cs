using System.Diagnostics.CodeAnalysis;
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
public sealed class PlansShortcutTests
{
    [Fact]
    public async Task AddTransactionShortcutOpensTheOnlyAvailablePlanAsync()
    {
        await using var context = CreateContext();
        var planId = Guid.NewGuid();
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns([new PlanSummary(planId, "Household", "", 3)]);
        context.Services.AddSingleton(budgets);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/plans?action=add-transaction");

        var component = context.Render<Plans>();

        await component.WaitForAssertionAsync(() => navigation.Uri.ShouldBe($"http://localhost/plans/{planId}/accounts?new=true"));
    }

    [Fact]
    public async Task AddTransactionShortcutChoosesAmongOwnedAndSharedPlansAsync()
    {
        await using var context = CreateContext();
        var owned = new PlanSummary(Guid.NewGuid(), "Household", "", 3);
        var shared = new PlanSummary(Guid.NewGuid(), "Beach trip", "", 5) { IsOwner = false, SharedBy = "Alice" };
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns([owned, shared]);
        context.Services.AddSingleton(budgets);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/plans?action=add-transaction");

        var component = context.Render<Plans>();

        await component.WaitForAssertionAsync(() => component.FindAll(".plan-card").Count.ShouldBe(2));
        component.FindAll(".plan-card").Select(card => card.GetAttribute("href"))
            .ShouldBe([$"/plans/{owned.Id}/accounts?new=true", $"/plans/{shared.Id}/accounts?new=true"]);
        component.FindAll(".plan-card-action").ShouldAllBe(link => link.TextContent.Contains("Add transaction", StringComparison.Ordinal));
        component.Find(".notice[role='status']").TextContent.ShouldContain("Choose the plan to add a transaction to");
        navigation.Uri.ShouldBe("http://localhost/plans?action=add-transaction");
    }

    [Fact]
    public async Task AddTransactionShortcutWithoutPlansKeepsTheCreateFirstPlanPromptAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        context.Services.AddSingleton(budgets);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/plans?action=add-transaction");

        var component = context.Render<Plans>();

        await component.WaitForAssertionAsync(() => component.Find(".empty-state").TextContent.ShouldContain("Create your first plan"));
        component.FindAll(".plan-card").ShouldBeEmpty();
        navigation.Uri.ShouldBe("http://localhost/plans?action=add-transaction");
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
