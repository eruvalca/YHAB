using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Pages;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PlanSharingComponentTests
{
    [Fact]
    public async Task PlansPageSeparatesOwnedAndSharedPlansAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new(Guid.NewGuid(), "Household", "", 3),
            new(Guid.NewGuid(), "Beach trip", "", 5) { IsOwner = false, SharedBy = "a@example.test" },
        ]);
        context.Services.AddSingleton(budgets);

        var component = context.Render<Plans>();

        await component.WaitForAssertionAsync(() => component.FindAll(".plan-card").Count.ShouldBe(2));
        component.Markup.ShouldContain("Your plans");
        component.Markup.ShouldContain("Shared with you");
        component.FindAll(".plan-card").Count.ShouldBe(2);
        component.Markup.ShouldContain("Shared by a@example.test");
        component.Markup.ShouldContain("Plans start private.");
    }

    [Fact]
    public async Task OwnerSeesShareFormAndCanRemoveACollaboratorAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("a@example.test", true, [new("owner-b", "b@example.test"), new("owner-c", "c@example.test")]));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#share-email").ShouldNotBeNull());
        component.Markup.ShouldContain("Everyone you add can open and edit this plan.");
        component.Markup.ShouldContain("b@example.test");
        component.Markup.ShouldContain("c@example.test");
        component.Find("#remove-person").ShouldNotBeNull();
        component.FindAll("#remove-person option").Count.ShouldBe(3);
        component.Markup.ShouldNotContain("Leave this plan");
    }

    [Fact]
    public async Task SharedMemberSeesOwnerAndLeaveConfirmationWithoutRemoveControlsAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: false, ownerName: "a@example.test");
        Configure(budgets, plan, new PlanShareSummary("a@example.test", false, [new("owner-b", "b@example.test")]));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#leave-confirm").ShouldNotBeNull());
        component.Markup.ShouldContain("This plan is shared with you by a@example.test.");
        component.Markup.ShouldContain("Leave this plan");
        component.Find("#leave-confirm").ShouldNotBeNull();
        component.FindAll("#share-email").ShouldBeEmpty();
        component.FindAll("#remove-person").ShouldBeEmpty();
    }

    private static void Configure(IBudgetClient budgets, PlanSnapshot plan, PlanShareSummary sharing)
    {
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(new PlanView(plan, [], false));
        budgets.ReadPayeesAsync(plan.Id, Arg.Any<CancellationToken>()).Returns([]);
        budgets.ReadSharingAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(sharing);
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static PlanSnapshot Plan(bool isOwner, string? ownerName = null)
    {
        var today = new DateOnly(2026, 10, 1);
        return new(Guid.NewGuid(), "Shared plan", "", today, 7, [], [], [], [], [], false, false, [])
        {
            Today = today,
            IsOwner = isOwner,
            OwnerName = ownerName,
        };
    }
}
