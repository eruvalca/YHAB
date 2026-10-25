using System.Diagnostics.CodeAnalysis;
using AngleSharp.Dom;
using Bunit;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AccountEditorTests
{
    private static readonly DateOnly _today = new(2026, 10, 3);

    [Fact]
    public async Task CancelAsksBeforeDiscardingTypedAccountDetailsAsync()
    {
        await using var context = CreateContext();
        var closes = 0;
        var component = Render(context, () => closes++);

        await component.FindAll("fluent-text-input")[0].ChangeAsync(new() { Value = "Renamed checking" });
        await Button(component, "Cancel").ClickAsync();

        closes.ShouldBe(0);
        component.Find("[role='alertdialog']").TextContent.ShouldContain("Discard your unsaved changes?");

        await Button(component, "Discard changes").ClickAsync();

        closes.ShouldBe(1);
    }

    [Fact]
    public async Task CancelClosesAnUnchangedAccountWithoutAskingAsync()
    {
        await using var context = CreateContext();
        var closes = 0;
        var component = Render(context, () => closes++);

        await Button(component, "Cancel").ClickAsync();

        closes.ShouldBe(1);
        component.FindAll("[role='alertdialog']").ShouldBeEmpty();
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<AccountEditor> Render(BunitContext context, Action onClose)
    {
        var plan = new PlanSnapshot(Guid.CreateVersion7(), "Plan", "", _today, 7, [], [], [], [], [], false, false, []) { Today = _today };
        var account = new AccountData(Guid.CreateVersion7(), "Checking", AccountKind.Checking, 100, _today, false, "");
        return context.Render<AccountEditor>(parameters => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.Account, account)
            .Add(item => item.OnCommand, _ => { })
            .Add(item => item.OnClose, () => onClose()));
    }

    private static IElement Button(IRenderedComponent<AccountEditor> component, string text)
        => component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), text, StringComparison.Ordinal));
}
