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
public sealed class CategoryEditorTests
{
    private static readonly DateOnly _today = new(2026, 10, 3);

    [Fact]
    public async Task DetailsShowsTheCategoryNameAndNotesAsync()
    {
        await using var context = CreateContext();
        var component = Render(context, CreatePlan(CreateCategory("Groceries", "Weekly shop")));

        NameField(component).GetAttribute("value").ShouldBe("Groceries");
        NotesField(component).GetAttribute("value").ShouldBe("Weekly shop");
    }

    [Fact]
    public async Task EmptyDetailsShowsEmptyFieldsWithFriendlyPlaceholdersAsync()
    {
        await using var context = CreateContext();
        var component = Render(context, CreatePlan(CreateCategory("", "")));

        NameField(component).GetAttribute("value").ShouldBeNullOrEmpty();
        NameField(component).GetAttribute("placeholder").ShouldBe("e.g. Groceries");
        NotesField(component).GetAttribute("value").ShouldBeNullOrEmpty();
        NotesField(component).GetAttribute("placeholder").ShouldBe("Anything worth remembering about this category");
    }

    [Fact]
    public async Task EditedNameAndNotesSaveAsync()
    {
        await using var context = CreateContext();
        var category = CreateCategory("Groceries", "Weekly shop");
        SaveCategory? saved = null;
        var component = Render(context, CreatePlan(category), command => saved = command as SaveCategory);

        await NameField(component).ChangeAsync(new() { Value = "Groceries and home" });
        await NotesField(component).ChangeAsync(new() { Value = "Every Saturday" });
        await SaveButton(component).ClickAsync();

        saved.ShouldNotBeNull();
        saved.Category.Name.ShouldBe("Groceries and home");
        saved.Category.Notes.ShouldBe("Every Saturday");
    }

    [Fact]
    public async Task ARefreshRenamesUntouchedFieldsWhileAnEditedNoteSurvivesAsync()
    {
        await using var context = CreateContext();
        var category = CreateCategory("Groceries", "Weekly shop");
        SaveCategory? saved = null;
        var component = Render(context, CreatePlan(category), command => saved = command as SaveCategory);

        await NotesField(component).ChangeAsync(new() { Value = "Every Saturday" });

        // An untouched name follows the refreshed record; an edited note is a user choice here.
        component.Render(parameters => Parameters(parameters, CreatePlan(category with { Name = "Food", Notes = "Updated elsewhere" }))
            .Add(item => item.OnCommand, command => saved = command as SaveCategory));
        NameField(component).GetAttribute("value").ShouldBe("Food");
        NotesField(component).GetAttribute("value").ShouldBe("Every Saturday");

        await SaveButton(component).ClickAsync();

        saved.ShouldNotBeNull();
        saved.Category.Name.ShouldBe("Food");
        saved.Category.Notes.ShouldBe("Every Saturday");
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<CategoryEditor> Render(
        BunitContext context,
        PlanSnapshot plan,
        Action<PlanCommand>? onCommand = null)
        => context.Render<CategoryEditor>(parameters => Parameters(parameters, plan)
            .Add(item => item.OnCommand, onCommand ?? (_ => { })));

    private static ComponentParameterCollectionBuilder<CategoryEditor> Parameters(
        ComponentParameterCollectionBuilder<CategoryEditor> parameters,
        PlanSnapshot plan)
        => parameters
            .Add(item => item.Plan, plan)
            .Add(item => item.Category, plan.Categories[0])
            .Add(item => item.Month, _today)
            .Add(item => item.OnClose, () => { });

    private static IElement NameField(IRenderedComponent<CategoryEditor> component)
        => component.Find(".category-options fluent-text-input");

    private static IElement NotesField(IRenderedComponent<CategoryEditor> component)
        => component.Find(".category-options fluent-textarea");

    private static IElement SaveButton(IRenderedComponent<CategoryEditor> component)
        => component.FindAll("fluent-button").Single(item => string.Equals(item.TextContent.Trim(), "Save category", StringComparison.Ordinal));

    private static PlanSnapshot CreatePlan(CategoryData category)
    {
        var group = new GroupData(category.GroupId, "Everyday", 0);
        return new(Guid.CreateVersion7(), "Plan", "", _today, 7, [], [group], [category], [], [], false, false, []) { Today = _today };
    }

    private static CategoryData CreateCategory(string name, string notes)
        => new(Guid.CreateVersion7(), Guid.CreateVersion7(), name, notes, 0, false, null, null);
}
