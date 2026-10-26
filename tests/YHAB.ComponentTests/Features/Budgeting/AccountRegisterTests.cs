using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
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
        var entry = new TransactionData(Guid.NewGuid(), account.Id, today, "Visible payee", "", -10, null, "", []);
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
    }

    [Fact]
    public async Task EmptyStateIsHiddenWhileTheRegisterIsBusyAsync()
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

        component.FindAll(".empty-state").ShouldBeEmpty();
        component.Find("[role='status']").TextContent.ShouldBe("Loading transactions…");
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
    public async Task RefreshKeepsAnOpenDraftAndACommandClosesItAsync()
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

        await component.FindAll("fluent-button").Single(item => item.TextContent.Contains("Add transaction", StringComparison.Ordinal)).ClickAsync();
        var payee = component.FindAll("fluent-text-input")[0];
        await payee.ChangeAsync(new() { Value = "Corner shop" });
        component.FindAll(".editor-surface").Count.ShouldBe(1);

        // A refresh applies a newer catalog without consuming the open draft.
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = 8 })
            .Add(item => item.ExternalRefresh, true));

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        component.FindAll("fluent-text-input")[0].GetAttribute("value").ShouldBe("Corner shop");
        await component.Find("fluent-button[aria-label='Close editor']").ClickAsync();
        component.Find("[role='alertdialog']").TextContent.ShouldContain("Discard your unsaved changes?");

        // A saved command consumes the draft, so the editor closes.
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = 9 })
            .Add(item => item.ExternalRefresh, false));

        component.FindAll(".editor-surface").ShouldBeEmpty();
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
        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "Corner shop edited" });

        // The workspace view loads its catalog without the ledger, so a refresh cannot prove that an
        // edited entry still exists. It must keep the draft instead of treating the omission as a delete.
        component.Render(parameters => parameters
            .Add(item => item.Plan, plan with { Version = 8, Transactions = [] })
            .Add(item => item.ExternalRefresh, true));

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        await component.Find("fluent-button[aria-label='Close editor']").ClickAsync();
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
        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "Corner shop" });

        // The refresh read fails: the draft is preserved, so recovery must not consume it either.
        component.Render(parameters => parameters.Add(item => item.Plan, plan with { Version = 8 }).Add(item => item.ExternalRefresh, true));
        component.Find("[role='alert']").TextContent.ShouldContain("Unable to load transactions");

        // A further refresh delivers the same version as a new instance, so only the recovered
        // read error would close the draft.
        component.Render(parameters => parameters.Add(item => item.Plan, plan with { Version = 8 }).Add(item => item.ExternalRefresh, true));

        component.FindAll(".editor-surface").Count.ShouldBe(1);
        component.FindAll("fluent-text-input")[0].GetAttribute("value").ShouldBe("Corner shop");
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
}
