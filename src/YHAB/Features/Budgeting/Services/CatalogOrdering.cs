using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class CatalogOrdering
{
    public static BudgetChangeOutcome Move(PlanSnapshot plan, ReorderGroup command)
    {
        var group = plan.Groups.SingleOrDefault(item => item.Id == command.GroupId);
        if (group is null || command.BeforeGroupId == command.GroupId
            || (command.BeforeGroupId.HasValue && !plan.Groups.Any(item => item.Id == command.BeforeGroupId)))
        {
            return new InvalidBudgetChange("Choose an existing group and a different position in this plan.");
        }

        var groups = plan.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Id)
            .Where(item => item.Id != group.Id).ToList();
        var index = command.BeforeGroupId.HasValue ? groups.FindIndex(item => item.Id == command.BeforeGroupId) : groups.Count;
        groups.Insert(index, group);
        return plan with { Groups = groups.Select((item, order) => item with { SortOrder = order }).ToArray() };
    }

    public static BudgetChangeOutcome Move(PlanSnapshot plan, ReorderCategory command)
    {
        var category = plan.Categories.SingleOrDefault(item => item.Id == command.CategoryId);
        if (category is null || !plan.Groups.Any(item => item.Id == command.GroupId)
            || command.BeforeCategoryId == command.CategoryId
            || (command.BeforeCategoryId.HasValue && !plan.Categories.Any(item => item.Id == command.BeforeCategoryId && item.GroupId == command.GroupId)))
        {
            return new InvalidBudgetChange("Choose an existing category, group, and destination in this plan.");
        }

        var destination = plan.Categories.Where(item => item.GroupId == command.GroupId && item.Id != category.Id)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToList();
        var index = command.BeforeCategoryId.HasValue ? destination.FindIndex(item => item.Id == command.BeforeCategoryId) : destination.Count;
        destination.Insert(index, category with { GroupId = command.GroupId });
        var ordered = destination.Select((item, order) => item with { SortOrder = order }).ToDictionary(item => item.Id);
        return plan with { Categories = plan.Categories.Select(item => ordered.GetValueOrDefault(item.Id, item)).ToArray() };
    }
}
