using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
public sealed class PlanSharingComponentTests
{
    [Fact]
    public async Task PlansPageSeparatesOwnedAndSharedPlansAndShowsJoinFormAsync()
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
        component.Markup.ShouldContain("Shared by a@example.test");
        component.Markup.ShouldContain("Plans start private.");
        component.Find("#join-plan-form").ShouldNotBeNull();
        component.Find("#join-code").ShouldNotBeNull();
    }

    [Fact]
    public async Task OwnerCreatesShareCodeAndRemovesACollaboratorAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        var sharing = new PlanShareSummary("a@example.test", true, [new("owner-b", "b@example.test"), new("owner-c", "c@example.test")]);
        Configure(budgets, plan, sharing);
        budgets.CreateShareCodeAsync(plan.Id, Arg.Any<CancellationToken>())
            .Returns(new PlanShareCode(plan.Id, "ABCDE-FGHIJ-KLMNO-PQRST", new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)));
        budgets.UnshareAsync(plan.Id, "owner-b", Arg.Any<CancellationToken>())
            .Returns(new PlanShareSummary("a@example.test", true, [new("owner-c", "c@example.test")]));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#create-share-code-form").ShouldNotBeNull());
        component.FindAll("#share-email").ShouldBeEmpty();
        component.Markup.ShouldContain("Create a one-time code");

        await component.Find("#create-share-code-form").SubmitAsync();

        await budgets.Received(1).CreateShareCodeAsync(plan.Id, Arg.Any<CancellationToken>());
        component.Markup.ShouldContain("ABCDE-FGHIJ-KLMNO-PQRST");
        component.Markup.ShouldContain("expires on");
        component.Find(".share-code").TextContent.ShouldBe("ABCDE-FGHIJ-KLMNO-PQRST");
        component.Find("[data-copy-share-code]").GetAttribute("type").ShouldBe("button");
        component.Find("[data-copy-share-code]").TextContent.ShouldBe("Copy code");
        component.Find("[data-copy-share-code-status]").GetAttribute("role").ShouldBe("status");

        SetFormValue(component.Instance, "Remove", "MemberId", "owner-b");
        await component.Find("#remove-share-form").SubmitAsync();

        await budgets.Received(1).UnshareAsync(plan.Id, "owner-b", Arg.Any<CancellationToken>());
        component.Markup.ShouldNotContain("b@example.test");
        component.Markup.ShouldContain("c@example.test");
        component.Markup.ShouldNotContain("Leave this plan");
    }

    [Fact]
    public async Task SharedMemberSeesOwnerAndRejectsUnconfirmedLeaveAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: false, ownerName: "a@example.test");
        Configure(budgets, plan, new PlanShareSummary("a@example.test", false, [new("owner-b", "b@example.test")]));
        budgets.LeaveAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#leave-plan-form").ShouldNotBeNull());
        component.Markup.ShouldContain("This plan is shared with you by a@example.test.");
        component.FindAll("#create-share-code-form").ShouldBeEmpty();
        component.FindAll("#remove-share-form").ShouldBeEmpty();

        await component.Find("#leave-plan-form").SubmitAsync();
        await budgets.DidNotReceive().LeaveAsync(plan.Id, Arg.Any<CancellationToken>());
        component.Markup.ShouldContain("Confirm that you want to leave this plan.");

        SetFormValue(component.Instance, "Leave", "Confirmed", true);
        await component.Find("#leave-plan-form").SubmitAsync();

        await budgets.Received(1).LeaveAsync(plan.Id, Arg.Any<CancellationToken>());
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/plans");
    }

    [Fact]
    public async Task PlansPageJoinsWithACodeAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var planId = Guid.NewGuid();
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        budgets.JoinAsync("ABCDE-FGHIJ", Arg.Any<CancellationToken>()).Returns(planId);
        context.Services.AddSingleton(budgets);

        var component = context.Render<Plans>();
        await component.WaitForAssertionAsync(() => component.Find("#join-plan-form").ShouldNotBeNull());
        SetFormValue(component.Instance, "Join", "Code", "ABCDE-FGHIJ");
        await component.Find("#join-plan-form").SubmitAsync();

        await budgets.Received(1).JoinAsync("ABCDE-FGHIJ", Arg.Any<CancellationToken>());
        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith($"/plans/{planId}");
    }

    [Fact]
    public async Task JoinServiceErrorIsShownAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        budgets.JoinAsync("BAD-CODE", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Guid>(new BudgetRequestException(404, "That share code is invalid or expired.")));
        context.Services.AddSingleton(budgets);

        var component = context.Render<Plans>();
        await component.WaitForAssertionAsync(() => component.Find("#join-plan-form").ShouldNotBeNull());
        SetFormValue(component.Instance, "Join", "Code", "BAD-CODE");
        await component.Find("#join-plan-form").SubmitAsync();

        component.Find(".notice[data-kind='error']").TextContent.ShouldContain("invalid or expired");
    }

    private static void SetFormValue(object page, string formProperty, string valueProperty, object? value)
    {
        var form = page.GetType().GetProperty(formProperty, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        form.GetType().GetProperty(valueProperty)!.SetValue(form, value);
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
