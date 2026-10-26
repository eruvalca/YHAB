using System.Diagnostics.CodeAnalysis;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.UI.Features.Budgeting.Components;

namespace YHAB.ComponentTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class EditorSurfaceTests
{
    [Fact]
    public async Task ClosingADirtyEditorAsksBeforeDiscardingAsync()
    {
        await using var context = CreateContext();
        var closes = 0;
        var component = Render(context, dirty: true, () => closes++);

        await CloseButton(component).ClickAsync();

        closes.ShouldBe(0);
        component.Find("[role='alertdialog']").TextContent.ShouldContain("Discard your unsaved changes?");

        await Button(component, "Keep editing").ClickAsync();

        component.FindAll("[role='alertdialog']").ShouldBeEmpty();
        closes.ShouldBe(0);

        await CloseButton(component).ClickAsync();
        await Button(component, "Discard changes").ClickAsync();

        closes.ShouldBe(1);
    }

    [Fact]
    public async Task ClosingACleanEditorClosesWithoutAskingAsync()
    {
        await using var context = CreateContext();
        var closes = 0;
        var component = Render(context, dirty: false, () => closes++);

        await CloseButton(component).ClickAsync();

        closes.ShouldBe(1);
        component.FindAll("[role='alertdialog']").ShouldBeEmpty();
    }

    [Fact]
    public async Task EscapeAsksOnADirtyEditorAndNeverDiscardsByItselfAsync()
    {
        await using var context = CreateContext();
        var dirtyCloses = 0;
        var dirty = Render(context, dirty: true, () => dirtyCloses++);

        await dirty.Find(".editor-surface").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        dirtyCloses.ShouldBe(0);
        dirty.Find("[role='alertdialog']").ShouldNotBeNull();

        // A repeated or held Escape must not stand in for the explicit discard action.
        await dirty.Find(".editor-surface").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        await CloseButton(dirty).ClickAsync();

        dirtyCloses.ShouldBe(0);
        dirty.Find("[role='alertdialog']").ShouldNotBeNull();

        await Button(dirty, "Discard changes").ClickAsync();

        dirtyCloses.ShouldBe(1);

        var cleanCloses = 0;
        var clean = Render(context, dirty: false, () => cleanCloses++);
        await clean.Find(".editor-surface").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        cleanCloses.ShouldBe(1);
        clean.FindAll("[role='alertdialog']").ShouldBeEmpty();
    }

    [Fact]
    public async Task OtherKeysDoNotCloseTheEditorAsync()
    {
        await using var context = CreateContext();
        var closes = 0;
        var component = Render(context, dirty: true, () => closes++);

        await component.Find(".editor-surface").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        closes.ShouldBe(0);
        component.FindAll("[role='alertdialog']").ShouldBeEmpty();
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<EditorSurface> Render(BunitContext context, bool dirty, Action onClose) =>
        context.Render<EditorSurface>(parameters => parameters
            .Add(item => item.Title, "Edit transaction")
            .Add(item => item.HasUnsavedChanges, () => dirty)
            .Add(item => item.OnClose, () => onClose())
            .Add(item => item.ChildContent, builder => builder.AddMarkupContent(0, "<p>Draft fields</p>")));

    private static IElement CloseButton(IRenderedComponent<EditorSurface> component)
        => component.Find("fluent-button[aria-label='Close editor']");

    private static IElement Button(IRenderedComponent<EditorSurface> component, string text)
        => component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), text, StringComparison.Ordinal));
}
