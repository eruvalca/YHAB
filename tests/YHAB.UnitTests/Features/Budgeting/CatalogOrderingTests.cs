using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Models;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CatalogOrderingTests
{
    [Fact]
    public void TargetFundingUsesReorderedGroupsBeforeCategoryPositions()
    {
        var plan = BudgetTestData.Create(50);
        var group = new GroupData(Guid.NewGuid(), "First priority", -1);
        var target = new TargetData(TargetKind.SetAside, TargetCadence.Monthly, 50, BudgetTestData.January, null, 0, DayOfWeek.Friday);
        var priority = new CategoryData(Guid.NewGuid(), group.Id, "Priority", "", 20, false, null, target);
        plan = plan with { Groups = [.. plan.Groups, group], Categories = [plan.Categories[0] with { Target = target }, priority] };
        var funded = MoneyChanges.AutoAssign(plan, new AutoAssign(plan.Version, BudgetTestData.January), plan.Today).AsT0;
        funded.Allocations.Single().CategoryId.ShouldBe(priority.Id);
        funded.Allocations.Single().Amount.ShouldBe(50);
    }

    [Fact]
    public void GroupMovesNormalizeTiedOrdersAndPreserveHiddenGroups()
    {
        var plan = BudgetTestData.Create();
        var first = plan.Groups[0];
        var second = new GroupData(Guid.NewGuid(), "Savings", 0, true);
        var third = new GroupData(Guid.NewGuid(), "Future", 20);
        plan = plan with { Groups = [first, second, third] };
        var moved = CatalogOrdering.Move(plan, new ReorderGroup(plan.Version, third.Id, first.Id)).AsT0;
        var groups = moved.Groups.OrderBy(item => item.SortOrder).ToArray();
        Array.IndexOf(groups, groups.Single(item => item.Id == third.Id)).ShouldBe(Array.IndexOf(groups, groups.Single(item => item.Id == first.Id)) - 1);
        groups.Select(item => item.SortOrder).ShouldBe([0, 1, 2]);
        groups.Single(item => item.Id == second.Id).Hidden.ShouldBeTrue();
        var end = CatalogOrdering.Move(moved, new ReorderGroup(plan.Version, third.Id, null)).AsT0;
        end.Groups.OrderBy(item => item.SortOrder).Last().Id.ShouldBe(third.Id);
        end.Categories.ShouldBe(plan.Categories);
    }

    [Fact]
    public void CategoryMoveAcrossGroupsPreservesTargetsAndFinancialHistory()
    {
        var plan = BudgetTestData.Assigned(BudgetTestData.Create(), 75);
        var target = new TargetData(TargetKind.SetAside, TargetCadence.Monthly, 150, BudgetTestData.January, null, 0, DayOfWeek.Friday);
        var category = plan.Categories[0] with { Notes = "Keep this", Target = target, Hidden = true };
        var group = new GroupData(Guid.NewGuid(), "Savings", 1);
        plan = plan with { Groups = [.. plan.Groups, group], Categories = [category, plan.Categories[1]], Transactions = [BudgetTestData.Entry(plan, 0, -20)] };
        var moved = CatalogOrdering.Move(plan, new ReorderCategory(plan.Version, category.Id, group.Id, null)).AsT0;
        moved.Categories.Single(item => item.Id == category.Id).ShouldBe(category with { GroupId = group.Id, SortOrder = 0 });
        moved.Categories.Single(item => item.Id != category.Id).ShouldBe(plan.Categories[1]);
        moved.Allocations.ShouldBe(plan.Allocations);
        moved.Transactions.ShouldBe(plan.Transactions);
        CheckpointPolicy.For(LedgerPatch.Between(plan, moved)).Value.ShouldBeOfType<PreserveCheckpoints>();
    }

    [Fact]
    public void CategoryMovesBeforeAnchorAndToEnd()
    {
        var plan = BudgetTestData.Create();
        var category = plan.Categories[1];
        var moved = CatalogOrdering.Move(plan, new ReorderCategory(plan.Version, category.Id, category.GroupId, plan.Categories[0].Id)).AsT0;
        moved.Categories.OrderBy(item => item.SortOrder).Select(item => item.Id).ShouldBe([category.Id, plan.Categories[0].Id]);
        var end = CatalogOrdering.Move(moved, new ReorderCategory(plan.Version, category.Id, category.GroupId, null)).AsT0;
        end.Categories.OrderBy(item => item.SortOrder).ShouldBe(plan.Categories);
    }

    [Fact]
    public void RejectsMissingForeignAndSelfDestinations()
    {
        var plan = BudgetTestData.Create();
        var category = plan.Categories[0];
        var group = plan.Groups[0];
        var foreign = Guid.NewGuid();
        CatalogOrdering.Move(plan, new ReorderGroup(plan.Version, foreign, null)).Value.ShouldBeOfType<InvalidBudgetChange>();
        CatalogOrdering.Move(plan, new ReorderGroup(plan.Version, group.Id, foreign)).Value.ShouldBeOfType<InvalidBudgetChange>();
        CatalogOrdering.Move(plan, new ReorderGroup(plan.Version, group.Id, group.Id)).Value.ShouldBeOfType<InvalidBudgetChange>();
        CatalogOrdering.Move(plan, new ReorderCategory(plan.Version, foreign, group.Id, null)).Value.ShouldBeOfType<InvalidBudgetChange>();
        CatalogOrdering.Move(plan, new ReorderCategory(plan.Version, category.Id, foreign, null)).Value.ShouldBeOfType<InvalidBudgetChange>();
        CatalogOrdering.Move(plan, new ReorderCategory(plan.Version, category.Id, group.Id, foreign)).Value.ShouldBeOfType<InvalidBudgetChange>();
        CatalogOrdering.Move(plan, new ReorderCategory(plan.Version, category.Id, group.Id, category.Id)).Value.ShouldBeOfType<InvalidBudgetChange>();
        var other = new GroupData(foreign, "Other", 1);
        plan = plan with { Groups = [group, other] };
        CatalogOrdering.Move(plan, new ReorderCategory(plan.Version, category.Id, other.Id, plan.Categories[1].Id)).Value.ShouldBeOfType<InvalidBudgetChange>();
    }
}
