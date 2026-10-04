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
public sealed class WorkspaceTests
{
    [Theory]
    [InlineData("Static", false)]
    [InlineData("Server", true)]
    [InlineData("WebAssembly", true)]
    public async Task DelayedSnapshotPostsDueRecurringOnlyWhenInteractiveAsync(string renderer, bool interactive)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.ComponentFactories.AddStub<FluentProviders>();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/YHAB.UI/budget-workspace.js");
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var recurring = new TransactionData(Guid.NewGuid(), account.Id, today, "Salary", "", 2000, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", [], RepeatFrequency.Monthly);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Delayed plan", "", today, 7, [account], [], [], [], [recurring], false, false, []) { Today = today };
        var posted = plan with
        {
            Version = 8,
            Transactions =
            [
                recurring with { Date = today.AddMonths(1) },
                recurring with { Id = Guid.NewGuid(), Repeat = RepeatFrequency.None, SourceTemplateId = recurring.Id, ScheduledDate = today },
            ],
        };
        var pendingRead = new TaskCompletionSource<PlanView>(TaskCreationOptions.RunContinuationsAsynchronously);
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(pendingRead.Task, Task.FromResult(View(posted, false)));
        budgets.ReadMonthAsync(plan.Id, Arg.Any<DateOnly>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => BudgetCalculator.Calculate(call.Arg<long>() == 7 ? plan : posted, call.Arg<DateOnly>(), today));
        ConfigureWorkspaceRead(budgets);
        context.Services.AddSingleton(budgets);
        context.Renderer.SetRendererInfo(new RendererInfo(renderer, interactive));

        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        component.Find("[role='status']").TextContent.ShouldBe("Opening your plan…");
        Commands(budgets).ShouldBeEmpty();

        pendingRead.SetResult(View(plan, true));
        await component.WaitForAssertionAsync(() => component.Find(".workspace").GetAttribute("data-interactive").ShouldBe(interactive ? "true" : "false"));
        if (interactive)
        {
            await component.WaitForAssertionAsync(() => Commands(budgets).Length.ShouldBe(1));
            var command = Commands(budgets).Single().ShouldBeOfType<PostRecurring>();
            command.Version.ShouldBe(7);
            command.ThroughDate.ShouldBe(today);
        }
        else
        {
            Commands(budgets).ShouldBeEmpty();
        }
        budgets.ReceivedCalls().Count(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadViewAsync), StringComparison.Ordinal)).ShouldBe(interactive ? 2 : 1);
        await component.WaitForAssertionAsync(() => component.Find(".hero-amount").TextContent.ShouldBe(interactive ? "$3,000.00" : "$1,000.00"));
    }

    private static PlanView View(PlanSnapshot plan, bool due = false) => new(plan with { Transactions = [] },
        plan.Accounts.Select(account => BudgetFacts.Balance(plan, account, plan.Today)).ToArray(), due);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransactionEditorsUsePayeeProjectionAndRefreshSuggestionsAsync(bool editing)
    {
        await using var context = new BunitContext();
        var (budgets, empty) = Configure(context);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, empty.Today, false, "");
        var plan = empty with { Accounts = [account] };
        var entry = new TransactionData(Guid.NewGuid(), account.Id, plan.Today, "Visible payee", "", -10, null,
            ClearingState.Uncleared, ClearingState.Uncleared, false, "", []);
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(View(plan), View(plan with { Version = 8 }));
        budgets.ReadRegisterAsync(plan.Id, Arg.Any<RegisterQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => new RegisterPage(call.Arg<RegisterQuery>().Version!.Value, [new(entry, 990)], 1, null));
        budgets.ReadPayeesAsync(plan.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["Visible payee", "Other account payee", "visible payee"]),
                Task.FromResult<IReadOnlyList<string>>(["Renamed payee"]));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/plans/{plan.Id}/accounts");
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        if (editing) { await component.Find(".register-table .category-link").ClickAsync(); }
        else { await Button(component, "+ Add transaction").ClickAsync(); }
        await component.WaitForAssertionAsync(() => component.FindAll("#known-payees option")
            .Select(item => item.GetAttribute("value")).ShouldBe(["Other account payee", "Visible payee"]));

        await Button(component, "Refresh").ClickAsync();
        await Button(component, "+ Add transaction").ClickAsync();
        await component.WaitForAssertionAsync(() => component.FindAll("#known-payees option")
            .Select(item => item.GetAttribute("value")).ShouldBe(["Renamed payee"]));
        budgets.ReceivedCalls().Count(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadPayeesAsync), StringComparison.Ordinal)).ShouldBe(2);
        budgets.ReceivedCalls().ShouldNotContain(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadAsync), StringComparison.Ordinal));
        budgets.ReceivedCalls().ShouldNotContain(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadMonthAsync), StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommittedUndoWithFailedRefreshDisablesEditingUntilRefreshAsync()
    {
        await using var context = new BunitContext();
        var (budgets, plan) = Configure(context);
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(View(plan)),
            Task.FromException<PlanView>(new HttpRequestException("Disconnected")), Task.FromResult(View(plan with { Version = 8, CanUndo = false, CanRedo = true })));
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await Button(component, "Undo").ClickAsync();
        component.Find("[role='alert']").TextContent.ShouldContain("was saved");
        Button(component, "Undo").HasAttribute("disabled").ShouldBeTrue();
        component.FindAll("fluent-button").ShouldNotContain(item => item.TextContent.Contains("Retry save", StringComparison.Ordinal));
        await Button(component, "Refresh plan").ClickAsync();
        component.FindAll("[role='alert']").ShouldBeEmpty();
        component.Find(".workspace-status").TextContent.ShouldBe("Change undone");
        Button(component, "Redo").HasAttribute("disabled").ShouldBeFalse();
        Commands(budgets).Length.ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SavedUndoRecoversReadConflictsWithoutResendingTheCommandAsync(int conflicts)
    {
        await using var context = new BunitContext();
        var (budgets, empty) = Configure(context);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, empty.Today, false, "");
        var plan = empty with { Accounts = [account] };
        var reads = 0;
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(_ => View(plan with
        {
            Version = 7 + reads++,
            Accounts = [account with { OpeningBalance = 1000 + (reads - 1) * 100 }],
        }));
        budgets.ReadMonthAsync(plan.Id, Arg.Any<DateOnly>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var version = call.Arg<long>();
            if (version > 7 && version <= 7 + conflicts) { throw new BudgetRequestException(409, "Changed between reads"); }
            return BudgetCalculator.Calculate(plan with { Accounts = [account with { OpeningBalance = 1000 + (version - 7) * 100 }] }, call.Arg<DateOnly>(), plan.Today);
        });
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        component.Find(".hero-amount").TextContent.ShouldBe("$1,000.00");

        await Button(component, "Undo").ClickAsync();

        Commands(budgets).Single().ShouldBeOfType<UndoChange>().Version.ShouldBe(7);
        reads.ShouldBe(conflicts + 2);
        component.FindAll("[role='alert']").ShouldBeEmpty();
        component.Find(".workspace-status").TextContent.ShouldBe("Change undone");
        var expected = conflicts == 1 ? "$1,200.00" : "$1,300.00";
        component.Find(".hero-amount").TextContent.ShouldBe(expected);
        component.FindComponent<UI.Features.Budgeting.Components.PlanNavigation>().Instance.Balances!.Single().Working.ShouldBe(conflicts == 1 ? 1200 : 1300);
        budgets.ReceivedCalls().Where(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadMonthAsync), StringComparison.Ordinal))
            .Select(call => (long)call.GetArguments()[2]!).ShouldBe(Enumerable.Range(7, conflicts + 2).Select(value => (long)value));
    }

    [Theory]
    [InlineData(409, 3)]
    [InlineData(403, 1)]
    public async Task FailedPostSaveMonthReadPreservesSavedStatusAndHasBoundedRetriesAsync(int status, int attempts)
    {
        await using var context = new BunitContext();
        var (budgets, plan) = Configure(context);
        var reads = 0;
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(_ => View(plan with { Version = 7 + reads++ }));
        var failing = true;
        budgets.ReadMonthAsync(plan.Id, Arg.Any<DateOnly>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (call.Arg<long>() > 7 && failing) { throw new BudgetRequestException(status, "Read failed"); }
            return BudgetCalculator.Calculate(plan, call.Arg<DateOnly>(), plan.Today);
        });
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await Button(component, "Undo").ClickAsync();

        reads.ShouldBe(attempts + 1);
        Commands(budgets).Length.ShouldBe(1);
        component.Find("[role='alert']").TextContent.ShouldContain("Your change was saved");
        component.Find("[role='alert']").TextContent.ShouldNotContain("try your change again");
        Button(component, "Undo").HasAttribute("disabled").ShouldBeTrue();
        component.FindAll("fluent-button").ShouldNotContain(item => item.TextContent.Contains("Retry save", StringComparison.Ordinal));
        failing = false;
        await Button(component, "Refresh plan").ClickAsync();
        component.FindAll("[role='alert']").ShouldBeEmpty();
        component.Find(".workspace-status").TextContent.ShouldBe("Change undone");
        Commands(budgets).Length.ShouldBe(1);
        reads.ShouldBe(attempts + 2);
    }

    [Fact]
    public async Task PostSaveRefreshKeepsTheSelectedMonthAndWaitsBeforeEnablingEditsAsync()
    {
        await using var context = new BunitContext();
        var (budgets, plan) = Configure(context);
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await component.Find("fluent-button[aria-label='Previous month']").ClickAsync();
        component.Find(".month-label").TextContent.ShouldBe("September 2026");
        var pending = new TaskCompletionSource<BudgetMonth>(TaskCreationOptions.RunContinuationsAsynchronously);
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(View(plan with { Version = 8 }));
        budgets.ReadMonthAsync(plan.Id, new DateOnly(2026, 9, 1), 8, Arg.Any<CancellationToken>()).Returns(pending.Task);

        var click = Button(component, "Undo").ClickAsync();
        await component.WaitForAssertionAsync(() => component.Find(".workspace-status").TextContent.ShouldBe("Saved. Updating balances…"));
        Button(component, "Undo").HasAttribute("disabled").ShouldBeTrue();
        await component.Instance.HistoryShortcutAsync(false);
        Commands(budgets).Length.ShouldBe(1);
        pending.SetResult(BudgetCalculator.Calculate(plan, new(2026, 9, 1), plan.Today));
        await click;
        component.Find(".month-label").TextContent.ShouldBe("September 2026");
        component.Find(".workspace-status").TextContent.ShouldBe("Change undone");
        budgets.ReceivedCalls().Where(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadMonthAsync), StringComparison.Ordinal))
            .Select(call => (DateOnly)call.GetArguments()[1]!).ShouldBe([new(2026, 10, 1), new(2026, 9, 1), new(2026, 9, 1)]);
    }

    [Fact]
    public async Task InterruptedUndoRetriesTheOriginalOperationAndDoesNotSendParallelChangesAsync()
    {
        await using var context = new BunitContext();
        var (budgets, plan) = Configure(context);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        budgets.ExecuteAsync(plan.Id, Arg.Any<PlanCommand>(), Arg.Any<CancellationToken>()).Returns(pending.Task, Task.CompletedTask);
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        var click = Button(component, "Undo").ClickAsync();
        await component.WaitForAssertionAsync(() => Button(component, "Undo").HasAttribute("disabled").ShouldBeTrue());
        Commands(budgets).Length.ShouldBe(1);
        pending.SetException(new HttpRequestException("Response lost"));
        await click;
        component.Find("[role='alert']").TextContent.ShouldContain("unknown");
        await component.Instance.HistoryShortcutAsync(false);
        Commands(budgets).Length.ShouldBe(1);
        await Button(component, "Retry save safely").ClickAsync();
        var commands = Commands(budgets);
        commands.Length.ShouldBe(2);
        commands[1].ShouldBeSameAs(commands[0]);
        commands[1].OperationId.ShouldBe(commands[0].OperationId);
        component.FindAll("[role='alert']").ShouldBeEmpty();
    }

    [Fact]
    public async Task ConflictingUndoDoesNotReportSuccessOrReloadOverTheUsersInputsAsync()
    {
        await using var context = new BunitContext();
        var (budgets, plan) = Configure(context);
        budgets.ExecuteAsync(plan.Id, Arg.Any<PlanCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new BudgetRequestException(409, "Conflict")));
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        await Button(component, "Undo").ClickAsync();
        component.Find("[role='alert']").TextContent.ShouldContain("changed in another tab");
        component.Find(".workspace-status").TextContent.ShouldNotContain("undone");
        budgets.ReceivedCalls().Count(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ReadViewAsync), StringComparison.Ordinal)).ShouldBe(1);
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Workspace> component, string name)
        => component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), name, StringComparison.Ordinal));

    [Fact]
    public async Task LateReadFromPreviousPlanCannotReplaceCurrentPlanAsync()
    {
        await using var context = new BunitContext();
        var (budgets, plan) = Configure(context);
        var pending = new TaskCompletionSource<PlanView>(TaskCreationOptions.RunContinuationsAsynchronously);
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(pending.Task);
        var other = plan with { Id = Guid.NewGuid(), Name = "Current plan", Version = 12 };
        budgets.ReadViewAsync(other.Id, Arg.Any<CancellationToken>()).Returns(View(other));
        budgets.ReadMonthAsync(other.Id, Arg.Any<DateOnly>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => BudgetCalculator.Calculate(other, call.Arg<DateOnly>(), other.Today));
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        component.Render(parameters => parameters.Add(item => item.PlanId, other.Id));
        await component.WaitForAssertionAsync(() => component.Find(".workspace").TextContent.ShouldContain("Current plan"));
        pending.SetResult(View(plan));
        await component.InvokeAsync(() => Task.CompletedTask);
        component.Find(".workspace").TextContent.ShouldContain("Current plan");
        component.Find(".workspace").TextContent.ShouldNotContain("Opening your plan");
    }

    [Fact]
    public async Task RefreshBlocksUndoUntilTheNewRevisionIsLoadedAsync()
    {
        await using var context = new BunitContext();
        var (budgets, plan) = Configure(context);
        var pending = new TaskCompletionSource<PlanView>(TaskCreationOptions.RunContinuationsAsynchronously);
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(View(plan)), pending.Task);
        var component = context.Render<Workspace>(parameters => parameters.Add(item => item.PlanId, plan.Id));
        var refresh = Button(component, "Refresh").ClickAsync();
        await component.WaitForAssertionAsync(() => Button(component, "Undo").HasAttribute("disabled").ShouldBeTrue());
        await component.Instance.HistoryShortcutAsync(false);
        Commands(budgets).ShouldBeEmpty();
        pending.SetResult(View(plan with { Version = 8 }));
        await refresh;
        await Button(component, "Undo").ClickAsync();
        Commands(budgets).Single().Version.ShouldBe(8);
    }

    private static (IBudgetClient Budgets, PlanSnapshot Plan) Configure(BunitContext context)
    {
        context.Services.AddFluentUIComponents();
        context.ComponentFactories.AddStub<FluentProviders>();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/YHAB.UI/budget-workspace.js");
        var today = new DateOnly(2026, 10, 3);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [], [], [], [], [], true, false, []) { Today = today };
        var budgets = Substitute.For<IBudgetClient>();
        budgets.ReadViewAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(View(plan));
        budgets.ReadMonthAsync(plan.Id, Arg.Any<DateOnly>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => BudgetCalculator.Calculate(plan, call.Arg<DateOnly>(), today));
        ConfigureWorkspaceRead(budgets);
        context.Services.AddSingleton(budgets);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        return (budgets, plan);
    }

    private static PlanCommand[] Commands(IBudgetClient budgets) => budgets.ReceivedCalls()
        .Where(call => string.Equals(call.GetMethodInfo().Name, nameof(IBudgetClient.ExecuteAsync), StringComparison.Ordinal))
        .Select(call => (PlanCommand)call.GetArguments()[1]!).ToArray();

    private static void ConfigureWorkspaceRead(IBudgetClient budgets)
    {
        // Compose the test's supplied projection fixtures. Database snapshot
        // isolation belongs to WorkspaceProjectionTests, not this UI substitute.
        budgets.ReadWorkspaceAsync(Arg.Any<Guid>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var id = call.Arg<Guid>();
            var token = call.Arg<CancellationToken>();
            var view = await budgets.ReadViewAsync(id, token);
            var month = call.Arg<DateOnly?>() ?? BudgetFacts.Month(view.Catalog.Today);
            return new PlanMonthView(view, await budgets.ReadMonthAsync(id, month, view.Catalog.Version, token));
        });
    }
}
