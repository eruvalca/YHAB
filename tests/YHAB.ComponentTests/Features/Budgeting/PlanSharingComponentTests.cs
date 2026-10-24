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
            new(Guid.NewGuid(), "Beach trip", "", 5) { IsOwner = false, SharedBy = "Alice" },
        ]);
        context.Services.AddSingleton(budgets);

        var component = context.Render<Plans>();

        await component.WaitForAssertionAsync(() => component.FindAll(".plan-card").Count.ShouldBe(2));
        component.Markup.ShouldContain("Your plans");
        component.Markup.ShouldContain("Shared with you");
        component.Find(".plan-card-shared").TextContent.ShouldBe("Shared by Alice");
        component.Find(".plan-card-shared").TextContent.ShouldNotContain("@");
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
        var sharing = new PlanShareSummary("Alice", true, [new("owner-b", "Bob"), new("owner-c", "Carol")]);
        Configure(budgets, plan, sharing);
        budgets.CreateShareCodeAsync(plan.Id, Arg.Any<CancellationToken>())
            .Returns(new PlanShareCode(plan.Id, "ABCDE-FGHIJ-KLMNO-PQRST", new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)));
        budgets.UnshareAsync(plan.Id, "owner-b", Arg.Any<CancellationToken>())
            .Returns(new PlanShareSummary("Alice", true, [new("owner-c", "Carol")]));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#create-share-code-form").ShouldNotBeNull());
        component.FindAll("#share-email").ShouldBeEmpty();
        component.Markup.ShouldContain("Create a one-time code");
        component.Find("#sharing-section").TextContent.ShouldContain("will see your display name (Alice), but never your email address");
        component.Find("#sharing-section").TextContent.ShouldNotContain("@");
        component.FindAll("#sharing-section .share-list li").Select(item => item.TextContent).ShouldBe(["Bob", "Carol"]);

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
        component.Markup.ShouldNotContain("Bob");
        component.Markup.ShouldContain("Carol");
        component.Markup.ShouldNotContain("Leave this plan");
    }

    [Fact]
    public async Task DuplicateCollaboratorNamesAreNumberedSoTheOwnerCanRemoveTheRightAccountAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true, [new("owner-b", "Member"), new("owner-c", "Member")]));
        budgets.UnshareAsync(plan.Id, "owner-c", Arg.Any<CancellationToken>())
            .Returns(new PlanShareSummary("Alice", true, [new("owner-b", "Member")]));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#remove-share-form").ShouldNotBeNull());
        component.FindAll("#sharing-section .share-list li").Select(item => item.TextContent).ShouldBe(["Member (1)", "Member (2)"]);
        var options = component.FindAll("#remove-person option");
        options.Select(option => option.GetAttribute("value")).ShouldBe(["", "owner-b", "owner-c"]);
        options.Select(option => option.TextContent).ShouldBe(["Choose a person", "Member (1)", "Member (2)"]);

        SetFormValue(component.Instance, "Remove", "MemberId", "owner-c");
        await component.Find("#remove-share-form").SubmitAsync();

        await budgets.Received(1).UnshareAsync(plan.Id, "owner-c", Arg.Any<CancellationToken>());
        component.FindAll("#sharing-section .share-list li").Select(item => item.TextContent).ShouldBe(["Member"]);
    }

    [Fact]
    public async Task GeneratedDuplicateLabelsDoNotCollideWithLiteralDisplayNamesAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true,
            [new("owner-b", "Member"), new("owner-c", "Member"), new("owner-d", "Member (1)")]));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#remove-share-form").ShouldNotBeNull());
        var labels = component.FindAll("#sharing-section .share-list li").Select(item => item.TextContent).ToArray();
        labels.ShouldBe(["Member (2)", "Member (3)", "Member (1)"]);
        labels.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(labels.Length);
        var options = component.FindAll("#remove-person option");
        options.Select(option => option.GetAttribute("value")).ShouldBe(["", "owner-b", "owner-c", "owner-d"]);
        options.Select(option => option.TextContent).ShouldBe(["Choose a person", "Member (2)", "Member (3)", "Member (1)"]);
    }

    [Fact]
    public async Task RemovingLastCollaboratorKeepsSuccessFeedbackInTheSharingSectionAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true, [new("owner-b", "Bob")]));
        budgets.UnshareAsync(plan.Id, "owner-b", Arg.Any<CancellationToken>())
            .Returns(new PlanShareSummary("Alice", true, []));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await component.WaitForAssertionAsync(() => component.Find("#remove-share-form").ShouldNotBeNull());
        SetFormValue(component.Instance, "Remove", "MemberId", "owner-b");

        await component.Find("#remove-share-form").SubmitAsync();

        var success = component.Find("#sharing-section .notice[data-kind='success']");
        success.TextContent.ShouldBe("That account can no longer open this plan.");
        success.GetAttribute("role").ShouldBe("status");
        component.FindAll("#remove-share-form").ShouldBeEmpty();
        component.Find("#sharing-section").TextContent.ShouldContain("No one else has access yet.");
    }

    [Fact]
    public async Task RemovingWithoutSelectionMarksPersonFieldInvalidAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true, [new("owner-b", "Bob")]));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await component.WaitForAssertionAsync(() => component.Find("#remove-share-form").ShouldNotBeNull());

        await component.Find("#remove-share-form").SubmitAsync();

        var select = component.Find("#remove-person");
        select.GetAttribute("aria-invalid").ShouldBe("true");
        select.GetAttribute("aria-describedby").ShouldBe("remove-person-error");
        select.ClassList.Contains("invalid").ShouldBeTrue();
        select.GetAttribute("data-validation-focus").ShouldNotBeNull();
        component.Find("#remove-person-error").TextContent.ShouldBe("Choose a person to remove.");
        await budgets.DidNotReceive().UnshareAsync(plan.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemovalServiceErrorMarksPersonFieldInvalidAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true, [new("owner-b", "Bob")]));
        budgets.UnshareAsync(plan.Id, "owner-b", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlanShareSummary>(new BudgetRequestException(404, "That account is no longer in this plan.")));
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await component.WaitForAssertionAsync(() => component.Find("#remove-share-form").ShouldNotBeNull());
        SetFormValue(component.Instance, "Remove", "MemberId", "owner-b");

        await component.Find("#remove-share-form").SubmitAsync();

        var select = component.Find("#remove-person");
        select.GetAttribute("aria-invalid").ShouldBe("true");
        select.GetAttribute("aria-describedby").ShouldBe("remove-person-error");
        select.ClassList.Contains("invalid").ShouldBeTrue();
        component.Find("#remove-person-error").TextContent.ShouldBe("That account is no longer in this plan.");
        component.FindAll(".notice[data-kind='error']").ShouldBeEmpty();
    }

    [Fact]
    public async Task SharedMemberSeesOwnerAndRejectsUnconfirmedLeaveAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: false, ownerName: "Alice");
        Configure(budgets, plan, new PlanShareSummary("Alice", false, [new("owner-b", "Bob")]));
        budgets.LeaveAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        context.Services.AddSingleton(budgets);

        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));

        await component.WaitForAssertionAsync(() => component.Find("#leave-plan-form").ShouldNotBeNull());
        component.Markup.ShouldContain("This plan is shared with you by Alice.");
        component.Find("#sharing-section").TextContent.ShouldNotContain("@");
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
    public async Task JoinServiceErrorIsShownNextToTheCodeFieldAsync()
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

        var error = component.Find("#join-code-error");
        error.TextContent.ShouldContain("invalid or expired");
        error.GetAttribute("role").ShouldBe("alert");
        error.Closest("#join-plan-form").ShouldNotBeNull();
        component.Find("#join-code").GetAttribute("aria-invalid").ShouldBe("true");
        component.Find("#join-code").GetAttribute("aria-describedby").ShouldBe("join-code-error");
        component.Find("#join-code").HasAttribute("data-validation-focus").ShouldBeTrue();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    public async Task JoinSessionAndRevisionErrorsRemainAtPageLevelAsync(int status)
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        budgets.JoinAsync("BAD-CODE", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Guid>(new BudgetRequestException(status, "Request failed.")));
        context.Services.AddSingleton(budgets);

        var component = context.Render<Plans>();
        await component.WaitForAssertionAsync(() => component.Find("#join-plan-form").ShouldNotBeNull());
        SetFormValue(component.Instance, "Join", "Code", "BAD-CODE");
        await component.Find("#join-plan-form").SubmitAsync();

        component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Request failed.");
        component.Find("#join-plan-form").QuerySelectorAll(".notice, .validation-message").ShouldBeEmpty();
        component.Find("#join-code").HasAttribute("aria-invalid").ShouldBeFalse();
    }

    [Fact]
    public async Task PlanSettingsErrorIsShownInsideTheSettingsSectionAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true, []));
        budgets.ExecuteAsync(plan.Id, Arg.Any<PlanCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new BudgetRequestException(400, "Settings could not be saved.")));
        context.Services.AddSingleton(budgets);
        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await component.WaitForAssertionAsync(() => component.Find("#plan-settings-section").ShouldNotBeNull());

        await component.Find("#plan-settings-section form").SubmitAsync();

        var error = component.Find("#plan-settings-section .notice[data-kind='error']");
        error.TextContent.ShouldBe("Settings could not be saved.");
        error.GetAttribute("role").ShouldBe("alert");
        component.Find("#rename-payee-section").QuerySelectorAll(".notice").ShouldBeEmpty();
    }

    [Fact]
    public async Task PlanSettingsRevisionErrorRemainsAtPageLevelAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true, []));
        budgets.ExecuteAsync(plan.Id, Arg.Any<PlanCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new BudgetRequestException(409, "Plan changed elsewhere.")));
        context.Services.AddSingleton(budgets);
        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await component.WaitForAssertionAsync(() => component.Find("#plan-settings-section").ShouldNotBeNull());

        await component.Find("#plan-settings-section form").SubmitAsync();

        component.Find(".notice[data-kind='error']").TextContent.ShouldBe("Plan changed elsewhere.");
        component.Find("#plan-settings-section").QuerySelectorAll(".notice").ShouldBeEmpty();
    }

    [Fact]
    public async Task SavedSettingsRefreshFailureKeepsTheSavedMessageAsync()
    {
        await using var context = CreateContext();
        var budgets = Substitute.For<IBudgetClient>();
        var plan = Plan(isOwner: true);
        Configure(budgets, plan, new PlanShareSummary("Alice", true, []));
        budgets.ExecuteAsync(plan.Id, Arg.Any<PlanCommand>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var reads = 0;
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            reads++ == 0
                ? Task.FromResult(new PlanView(plan, [], false))
                : Task.FromException<PlanView>(new BudgetRequestException(400, "Refresh failed.")));
        context.Services.AddSingleton(budgets);
        var component = context.Render<PlanSettings>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await component.WaitForAssertionAsync(() => component.Find("#plan-settings-section").ShouldNotBeNull());

        await component.Find("#plan-settings-section form").SubmitAsync();

        component.Find(".notice[data-kind='error']").TextContent
            .ShouldBe("Your change was saved. Reload to update the displayed settings.");
        component.Find("#plan-settings-section").QuerySelectorAll(".notice").ShouldBeEmpty();
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
