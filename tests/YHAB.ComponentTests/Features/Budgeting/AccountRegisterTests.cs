using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountRegisterTests
{
    [Fact]
    public async Task EmptyRegisterRendersEmptyStateWithoutAHeaderOnlyTableAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        component.FindAll(".register-table").ShouldBeEmpty();
        component.FindAll("th").ShouldBeEmpty();
        component.FindAll(".pagination").ShouldBeEmpty();
        component.Find(".empty-state").TextContent.ShouldContain("No transactions here yet");
    }

    [Fact]
    public async Task RowMenuHoldsDuplicateInsteadOfRepeatingItInlineAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, new DateOnly(2025, 10, 3), "Visible payee", "Bus fare", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        var row = component.Find(".register-table tbody tr");
        var menu = row.QuerySelector("details.row-actions");
        menu.ShouldNotBeNull();
        row.QuerySelectorAll("fluent-button").ShouldBeEmpty();
        row.QuerySelectorAll(".row-actions-menu button").Select(button => button.TextContent.Trim()).ShouldBe(["Duplicate…"]);
        menu.QuerySelector(".visually-hidden").ShouldNotBeNull().TextContent.ShouldBe("Actions for Visible payee");
        row.QuerySelector("fluent-checkbox").ShouldNotBeNull();
        row.QuerySelectorAll("td")[4].TextContent.ShouldContain("Outflow");
        row.QuerySelector(".mobile-meta").ShouldNotBeNull().TextContent.ShouldContain("10/03/2025");
        row.QuerySelector(".mobile-meta").ShouldNotBeNull().TextContent.ShouldContain("Transfer / adjustment");
        row.QuerySelector(".mobile-meta").ShouldNotBeNull().TextContent.ShouldContain("Bus fare");
    }

    [Fact]
    public async Task DesktopRowSelectsOnceThenOpensInPlaceAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        var rowButton = component.Find(".register-table .category-link");
        await rowButton.ClickAsync();
        component.Find(".register-table tbody tr").GetAttribute("data-active").ShouldBe("true");
        component.FindAll(".transaction-editing-row").ShouldBeEmpty();

        await component.Find(".register-table .category-link").ClickAsync();
        component.FindAll(".transaction-editing-row").Count.ShouldBe(1);
        component.Find(".transaction-editing-row .transaction-editor-inline").ShouldNotBeNull();
        component.Find(".transaction-editing-row fluent-dropdown[placeholder='Choose a category']").ShouldNotBeNull();
    }

    [Fact]
    public async Task OverlappingClicksEnterThePendingRowEditorAsync()
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var javascript = new RegisterPlacementJSRuntime(hold);
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton<IJSRuntime>(javascript);
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        var first = component.Find(".register-table .category-link").ClickAsync();
        var second = component.Find(".register-table .category-link").ClickAsync();
        await second;
        component.FindAll(".transaction-editing-row").Count.ShouldBe(1);

        hold.SetResult(false);
        await first;
        component.FindAll(".transaction-editing-row").Count.ShouldBe(1);
    }

    [Fact]
    public async Task MobileActivationMountsAnEditorWhenTheRowLeavesTheCurrentPageAsync()
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var javascript = new RegisterPlacementJSRuntime(hold);
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton<IJSRuntime>(javascript);
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
        var other = new TransactionData(Guid.NewGuid(), account.Id, today.AddDays(-1), "Other payee", "", -5, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (query, _) => Task.FromResult(string.Equals(query.Search, "Other", StringComparison.Ordinal)
                ? new RegisterPage(plan.Version, [new(other, 985)], 1, null)
                : new RegisterPage(plan.Version, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        var activation = component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".table-toolbar fluent-text-input").ChangeAsync(new() { Value = "Other" });
        component.Find(".register-table .category-link").TextContent.ShouldBe("Other payee");

        hold.SetResult(true);
        await activation;

        component.FindAll(".transaction-editing-row").Count.ShouldBe(1);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Visible payee");
        component.Find("fluent-button[aria-label='Cancel']").ShouldNotBeNull();

        var editor = component.FindComponent<TransactionEditor>().Instance;
        await component.Find(".transaction-ledger-payee fluent-text-input").ChangeAsync(new() { Value = "Unsaved payee" });
        await component.Find(".table-toolbar fluent-text-input").ChangeAsync(new() { Value = "" });

        component.FindComponent<TransactionEditor>().Instance.ShouldBeSameAs(editor);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Unsaved payee");
        component.Find(".register-table tbody tr.transaction-editing-row").ShouldNotBeNull();
    }

    [Fact]
    public async Task AFilteredResultKeepsTheEditedDraftMountedUntilCloseAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var edited = new TransactionData(Guid.NewGuid(), account.Id, today, "Corner shop", "", -10, null, "", []);
        var matching = new TransactionData(Guid.NewGuid(), account.Id, today.AddDays(-1), "Hardware store", "", -20, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (query, _) => Task.FromResult(string.Equals(query.Search, "Hardware", StringComparison.Ordinal)
                ? new RegisterPage(plan.Version, [new(matching, 970)], 1, null)
                : new RegisterPage(plan.Version, [new(edited, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".transaction-ledger-payee fluent-text-input").ChangeAsync(new() { Value = "Corner market" });

        await component.Find(".table-toolbar fluent-text-input").ChangeAsync(new() { Value = "Hardware" });

        component.FindAll(".transaction-editing-row").Count.ShouldBe(1);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Corner market");
        await component.Find("fluent-button[aria-label='Cancel']").ClickAsync();
        await component.Find(".editor-discard fluent-button").ClickAsync();

        await component.WaitForAssertionAsync(() =>
        {
            component.FindAll(".transaction-editing-row").ShouldBeEmpty();
            component.Find(".register-table .category-link").TextContent.ShouldBe("Hardware store");
        });
    }

    [Fact]
    public async Task AccountAndFilterChangesKeepAnExistingTransactionDraftAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var source = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var destination = new AccountData(Guid.NewGuid(), "Savings", AccountKind.Savings, 500, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), source.Id, today, "Draft payee", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [source, destination], [], [], [], [entry], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, source.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".register-table .category-link").ClickAsync();
        var editor = component.FindComponent<TransactionEditor>().Instance;
        await component.Find(".transaction-ledger-payee fluent-text-input").ChangeAsync(new() { Value = "Unsaved draft" });

        component.Render(parameters => parameters
            .Add(item => item.AccountId, destination.Id)
            .Add(item => item.InitialFrom, today.AddDays(-7)));

        component.FindComponent<TransactionEditor>().Instance.ShouldBeSameAs(editor);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Unsaved draft");
    }

    [Fact]
    public async Task PagingIsDisabledWhileEditingATransactionAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
        var next = new RegisterCursor(today, 1, Guid.NewGuid(), "Another payee", -5);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(entry, 990)], 2, next)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".register-table .category-link").ClickAsync();

        component.FindAll(".pagination fluent-button").ShouldAllBe(button => button.HasAttribute("disabled"));
        component.Find("fluent-button[aria-label='Cancel']").TextContent.Trim().ShouldBe("Cancel");
    }

#pragma warning disable BL0016
    private sealed class RegisterPlacementJSRuntime(TaskCompletionSource<bool> hold) : IJSRuntime, IDisposable
    {
        private readonly Module _module = new(hold);

        public void Dispose() => _module.DisposeAsync().AsTask().GetAwaiter().GetResult();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => string.Equals(identifier, "import", StringComparison.Ordinal)
                ? ValueTask.FromResult((TValue)(object)_module)
                : ValueTask.FromResult(default(TValue)!);

        private sealed class Module(TaskCompletionSource<bool> hold) : IJSObjectReference
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
                => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
                => string.Equals(identifier, "compactRegister", StringComparison.Ordinal)
                    ? new(AnswerAsync<TValue>(hold.Task))
                    : ValueTask.FromResult(default(TValue)!);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            private static async Task<TValue> AnswerAsync<TValue>(Task<bool> answer)
                => (TValue)(object)await answer;
        }
    }
#pragma warning restore BL0016

    [Fact]
    public async Task LoadedEmptyStateStaysPaintedWhileTheWorkspaceIsBusyAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        component.Find(".empty-state").ShouldNotBeNull();
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.Busy, true)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        component.Find(".empty-state").TextContent.ShouldContain("No transactions here yet");
        component.FindAll(".register-loading").ShouldBeEmpty();
        component.Find(".register-results").GetAttribute("aria-busy").ShouldBe("true");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task RevisionRefreshRetainsRowsUnlessAccessIsRevokedAsync(int status)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Old account name", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var pending = new TaskCompletionSource<RegisterPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (query, _) => query.Version == 7
                ? Task.FromResult(new RegisterPage(7, [new(entry, 990)], 1, null))
                : pending.Task)
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        component.Render(parameters => parameters.Add(item => item.Plan, plan with { Version = 8, Accounts = [] }));
        component.Find(".register-table tbody").TextContent.ShouldContain("Old account name");
        component.Find(".register-table .category-link").HasAttribute("disabled").ShouldBeTrue();
        component.FindAll(".register-loading").ShouldBeEmpty();
        component.Find(".register-results").GetAttribute("aria-busy").ShouldBe("true");

        if (status == 500) { pending.SetException(new HttpRequestException("offline")); }
        else if (status != 0) { pending.SetException(new BudgetRequestException(status, "Access denied")); }
        else { pending.SetResult(new RegisterPage(8, [], 0, null)); }

        await component.WaitForAssertionAsync(() =>
        {
            if (status == 500)
            {
                component.Find("[role='alert']").TextContent.ShouldContain("Unable to load transactions");
                component.Find(".register-table tbody").TextContent.ShouldContain("Visible payee");
            }
            else if (status != 0)
            {
                component.Find("[role='alert']").TextContent.ShouldContain("Access denied");
                component.FindAll(".register-table").ShouldBeEmpty();
                component.FindAll(".empty-state").ShouldBeEmpty();
            }
            else
            {
                component.FindAll(".register-table").ShouldBeEmpty();
                component.Find(".empty-state").ShouldNotBeNull();
                component.Find(".register-results").GetAttribute("aria-busy").ShouldBe("false");
            }
        });
    }

    [Fact]
    public async Task RowsPaintBeforeDelayedPayeeSuggestionsAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(7, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => pending.Task));

        component.Find(".register-table tbody").TextContent.ShouldContain("Visible payee");
        component.FindAll(".register-loading").ShouldBeEmpty();
        pending.SetResult(["Visible payee"]);
        await component.WaitForAssertionAsync(() => component.Find(".register-results").GetAttribute("aria-busy").ShouldBe("false"));
    }

    [Fact]
    public async Task ReportFiltersReachTheRegisterQueryAndCanBeClearedAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var group = new GroupData(Guid.NewGuid(), "Everyday", 0);
        var category = new CategoryData(Guid.NewGuid(), group.Id, "Groceries", "", 0, false, null, null);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [group], [category], [], [], false, false, []) { Today = today };
        var queries = new List<RegisterQuery>();
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.InitialCategoryId, category.Id).Add(item => item.InitialFrom, new DateOnly(2026, 10, 1))
            .Add(item => item.InitialThrough, today).Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (query, _) => { queries.Add(query); return Task.FromResult(new RegisterPage(plan.Version, [], 0, null)); })
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        queries.Single().CategoryId.ShouldBe(category.Id);
        queries.Single().From.ShouldBe(new DateOnly(2026, 10, 1));
        queries.Single().Through.ShouldBe(today);
        component.Find(".notice").TextContent.ShouldContain("Filtered to Groceries");
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Clear report filters", StringComparison.Ordinal)).ClickAsync();
        queries[^1].CategoryId.ShouldBeNull();
        queries[^1].From.ShouldBeNull();
        queries[^1].Through.ShouldBeNull();
        component.FindAll(".notice").ShouldBeEmpty();
    }

    [Fact]
    public async Task RefreshKeepsAnOpenDraftAndItsOwnSaveClosesItAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var commands = new List<PlanCommand>();
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Add transaction", StringComparison.Ordinal)).ClickAsync();
        var payee = component.Find(".transaction-ledger-payee fluent-text-input");
        await payee.ChangeAsync(new() { Value = "Corner shop" });
        component.FindAll(".editor-surface").Count.ShouldBe(1);

        // A refresh applies a newer catalog without consuming the open draft.
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = 8 })
            .Add(item => item.ExternalRefresh, true));

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Corner shop");
        var direction = component.FindAll("fluent-dropdown").First(item => item.TextContent.Contains("Outflow", StringComparison.Ordinal));
        var inflow = direction.QuerySelector("fluent-option[text='Inflow']").ShouldNotBeNull();
        await direction.TriggerEventAsync("ondropdownchange", new DropdownEventArgs { SelectedOptions = inflow.Id });
        await component.FindAll(".transaction-ledger-amount fluent-text-input")[1].ChangeAsync(new() { Value = "12.50" });
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Save transaction", StringComparison.Ordinal)).ClickAsync();
        var submitted = commands.Single().ShouldBeOfType<SaveTransaction>();

        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = 8 })
            .Add(item => item.ExternalRefresh, false)
            .Add(item => item.AppliedCommand, new UndoChange(plan.Version)));
        component.FindAll(".editor-surface").Count.ShouldBe(1);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Corner shop");

        // A saved command consumes the draft, so the editor closes.
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = 9 })
            .Add(item => item.ExternalRefresh, false)
            .Add(item => item.AppliedCommand, submitted));

        component.FindAll(".editor-surface").ShouldBeEmpty();
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Add transaction", StringComparison.Ordinal)).ClickAsync();
        component.FindAll(".editor-surface").Count.ShouldBe(1);
    }

    [Fact]
    public async Task TransactionEntryCanReopenAfterItsEditorClosesAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        var add = component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Add transaction", StringComparison.Ordinal));
        await add.ClickAsync();
        await component.Find("fluent-button[aria-label='Cancel']").ClickAsync();
        component.FindAll(".editor-surface").ShouldBeEmpty();

        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Add transaction", StringComparison.Ordinal)).ClickAsync();
        component.FindAll(".editor-surface").Count.ShouldBe(1);
    }

    [Fact]
    public async Task AppliedDeleteClosesAnEditorForADeletedTransactionAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var deleted = new TransactionData(Guid.NewGuid(), account.Id, today, "Delete me", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [deleted], false, false, []) { Today = today };
        var commands = new List<PlanCommand>();
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(deleted, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".register-table fluent-checkbox").ChangeAsync(new() { Value = true });
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Delete…", StringComparison.Ordinal)).ClickAsync();
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Delete selected transactions", StringComparison.Ordinal)).ClickAsync();
        var submitted = commands.Single().ShouldBeOfType<DeleteTransactions>();

        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = plan.Version + 1 })
            .Add(item => item.AppliedCommand, submitted));

        component.FindAll(".editor-surface").ShouldBeEmpty();
        component.FindAll(".notice[data-kind='warning']").ShouldBeEmpty();
        component.FindAll(".selection-toolbar").ShouldBeEmpty();
    }

    [Fact]
    public async Task AppliedDeletePreservesAnUnrelatedEditorAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var edited = new TransactionData(Guid.NewGuid(), account.Id, today, "Keep editing", "", -10, null, "", []);
        var deleted = new TransactionData(Guid.NewGuid(), account.Id, today.AddDays(-1), "Delete me", "", -20, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [edited, deleted], false, false, []) { Today = today };
        var commands = new List<PlanCommand>();
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, commands.Add).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(edited, 990), new(deleted, 970)], 2, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.FindAll(".register-table .category-link")[0].ClickAsync();
        await component.FindAll(".register-table .category-link")[0].ClickAsync();
        await component.FindAll(".register-table tbody tr")[1].QuerySelector("fluent-checkbox").ShouldNotBeNull()
            .ChangeAsync(new() { Value = true });
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Delete…", StringComparison.Ordinal)).ClickAsync();
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Delete selected transactions", StringComparison.Ordinal)).ClickAsync();
        var submitted = commands.Single().ShouldBeOfType<DeleteTransactions>();

        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = plan.Version + 1 })
            .Add(item => item.AppliedCommand, submitted));

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Keep editing");
        component.FindAll(".notice[data-kind='warning']").ShouldBeEmpty();
        component.FindAll(".selection-toolbar").ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteConfirmationCannotSubmitWithNoSelectionAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [entry], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.Find(".register-table fluent-checkbox").ChangeAsync(new() { Value = true });
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Delete…", StringComparison.Ordinal)).ClickAsync();
        await component.Find(".register-table fluent-checkbox").ChangeAsync(new() { Value = false });

        var delete = component.FindAll("fluent-button")
            .Single(item => item.TextContent.Contains("Delete selected transactions", StringComparison.Ordinal));
        delete.HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task RefreshKeepsAnEditedDraftWhenTheCatalogOmitsTheLedgerAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Corner shop", "", -10, null, "", []);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [entry], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [new(entry, 990)], 1, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        await component.WaitForAssertionAsync(() => component.FindAll(".register-table .category-link").Count.ShouldBe(1));
        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".register-table .category-link").ClickAsync();
        await component.Find(".transaction-ledger-payee fluent-text-input").ChangeAsync(new() { Value = "Corner shop edited" });

        // The workspace view loads its catalog without the ledger, so a refresh cannot prove that an
        // edited entry still exists. It must keep the draft instead of treating the omission as a delete.
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = 8, Transactions = [] })
            .Add(item => item.ExternalRefresh, true));

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        await component.Find("fluent-button[aria-label='Cancel']").ClickAsync();
        component.Find("[role='alertdialog']").TextContent.ShouldContain("Discard your unsaved changes?");
    }

    [Fact]
    public async Task RefreshKeepsADraftWhileAFailedReadRecoversAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var reads = 0;
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => ReadPageAsync())
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        Task<RegisterPage> ReadPageAsync() => ++reads == 1
            ? Task.FromResult(new RegisterPage(plan.Version, [], 0, null))
            : throw new HttpRequestException("offline");

        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Add transaction", StringComparison.Ordinal)).ClickAsync();
        await component.Find(".transaction-ledger-payee fluent-text-input").ChangeAsync(new() { Value = "Corner shop" });

        // The refresh read fails: the draft is preserved, so recovery must not consume it either.
        component.Render(parameters => parameters.Add(item => item.Plan, plan with { Version = 8 }).Add(item => item.ExternalRefresh, true));
        component.Find("[role='alert']").TextContent.ShouldContain("Unable to load transactions");

        // A further refresh delivers the same version as a new instance, so only the recovered
        // read error would close the draft.
        component.Render(parameters => parameters.Add(item => item.Plan, plan with { Version = 8 }).Add(item => item.ExternalRefresh, true));

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        component.Find(".transaction-ledger-payee fluent-text-input").GetAttribute("value").ShouldBe("Corner shop");
    }

    [Fact]
    public async Task UnknownCategoryFilterFallsBackAndCanBeClearedAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var queries = new List<RegisterQuery>();
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.InitialCategoryId, Guid.NewGuid()).Add(item => item.InitialFrom, new DateOnly(2026, 10, 1))
            .Add(item => item.InitialThrough, today).Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (query, _) => { queries.Add(query); return Task.FromResult(new RegisterPage(plan.Version, [], 0, null)); })
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        component.Find(".notice").TextContent.ShouldContain("an unavailable category");
        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Clear report filters", StringComparison.Ordinal)).ClickAsync();
        queries[^1].CategoryId.ShouldBeNull();
        component.FindAll(".notice").ShouldBeEmpty();
    }

    [Fact]
    public async Task TheDockStepsAsideForTheAccountEditorAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        component.Find(".add-transaction-dock").GetAttribute("data-editing").ShouldBe("false");

        // The account editor pins its own actions to the bottom of a phone screen, so the dock
        // steps aside instead of covering them.
        component.Render(parameters => parameters.Add(item => item.AccountEditing, true));
        component.Find(".add-transaction-dock").GetAttribute("data-editing").ShouldBe("true");
    }

    [Fact]
    public async Task AddTransactionStaysInThumbReachWhileTheRegisterScrollsAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([])));

        // The narrow-screen rule docks this wrapper to the viewport edge, so the
        // markup has to expose it and stop doing so once its editor is open.
        var dock = component.Find(".add-transaction-dock");
        dock.GetAttribute("data-editing").ShouldBe("false");
        component.FindAll(".add-transaction-dock .add-transaction-action").Count.ShouldBe(1);

        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Add transaction", StringComparison.Ordinal)).ClickAsync();

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        component.Find(".add-transaction-dock").GetAttribute("data-editing").ShouldBe("true");
        component.Find(".transaction-editing-row").ParentElement.ShouldNotBeNull();
        component.Find(".transaction-editing-row .transaction-editor-inline").ShouldNotBeNull();
        component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Cancel", StringComparison.Ordinal))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task ShortcutIntentOpensTheEditorOnceAndReportsItConsumedAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var consumed = 0;
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(item => item.InitialNewTransaction, true)
            .Add(item => item.OnNewTransactionConsumed, () => consumed++));

        await component.WaitForAssertionAsync(() => component.FindAll(".editor-surface").Count.ShouldBe(1));
        component.Find(".editor-heading h2").TextContent.ShouldBe("Add transaction");
        consumed.ShouldBe(1);

        await component.Find("fluent-button[aria-label='Cancel']").ClickAsync();
        component.FindAll(".editor-surface").ShouldBeEmpty();

        // A later render from the same caller must not reopen the consumed intent.
        component.Render(parameters => parameters.Add(item => item.InitialNewTransaction, true));
        component.FindAll(".editor-surface").ShouldBeEmpty();
        consumed.ShouldBe(1);

        // Clearing and reissuing the shortcut is a new intent, so the same mounted register can
        // open transaction entry again.
        component.Render(parameters => parameters.Add(item => item.InitialNewTransaction, false));
        component.Render(parameters => parameters.Add(item => item.InitialNewTransaction, true));
        await component.WaitForAssertionAsync(() => component.FindAll(".editor-surface").Count.ShouldBe(1));
        consumed.ShouldBe(2);
    }

    [Fact]
    public async Task ShortcutIntentWaitsForABlockingAccountEditorAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var consumed = 0;
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(item => item.InitialNewTransaction, true)
            .Add(item => item.OnNewTransactionConsumed, () => consumed++)
            .Add(item => item.AccountEditing, true));

        await component.WaitForAssertionAsync(() =>
        {
            component.FindAll(".editor-surface").ShouldBeEmpty();
            consumed.ShouldBe(0);
        });

        component.Render(parameters => parameters.Add(item => item.AccountEditing, false));
        await component.WaitForAssertionAsync(() =>
        {
            component.FindAll(".editor-surface").Count.ShouldBe(1);
            consumed.ShouldBe(1);
        });
    }

    [Fact]
    public async Task ShortcutIntentWithoutAnAccountExplainsHowToAddOneAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var today = new DateOnly(2026, 10, 3);
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [], [], [], [], [], false, false, []) { Today = today };
        var consumed = 0;
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(item => item.InitialNewTransaction, true)
            .Add(item => item.OnNewTransactionConsumed, () => consumed++));

        await component.WaitForAssertionAsync(() => component.Find(".notice[role='status']").TextContent.ShouldContain("Add an account"));
        component.FindAll(".editor-surface").ShouldBeEmpty();
        consumed.ShouldBe(1);
    }

    [Fact]
    public async Task PrerenderedShortcutIntentLeavesTheEditorClosedAsync()
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Renderer.SetRendererInfo(new RendererInfo("Server", false));
        var today = new DateOnly(2026, 10, 3);
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 1000, today, false, "");
        var plan = new PlanSnapshot(Guid.NewGuid(), "Plan", "", today, 7, [account], [], [], [], [], false, false, []) { Today = today };
        var consumed = 0;
        var component = context.Render<AccountRegister>(parameters => parameters
            .Add(item => item.Plan, plan).Add(item => item.AccountId, account.Id)
            .Add(item => item.OnCommand, _ => { }).Add(item => item.OnEditAccount, _ => { })
            .Add(item => item.LoadPage, (_, _) => Task.FromResult(new RegisterPage(plan.Version, [], 0, null)))
            .Add(item => item.LoadPayees, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(item => item.InitialNewTransaction, true)
            .Add(item => item.OnNewTransactionConsumed, () => consumed++));

        await component.WaitForAssertionAsync(() => component.Find(".empty-state").ShouldNotBeNull());
        component.FindAll(".editor-surface").ShouldBeEmpty();
        consumed.ShouldBe(0);
    }
}
