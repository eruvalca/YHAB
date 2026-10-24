using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class PlanAccess
{
    extension(IQueryable<BudgetPlan> plans)
    {
        // Plans stay visible to their owner and to every account the owner shares them with.
        public IQueryable<BudgetPlan> WhereAccessibleBy(string userId)
            => plans.Where(item => item.OwnerId == userId || item.Members.Any(member => member.UserId == userId));
    }

    public static async Task<bool> CanAccessAsync(ApplicationDbContext database, BudgetPlan plan, string userId, CancellationToken cancellationToken)
        => string.Equals(plan.OwnerId, userId, StringComparison.Ordinal)
            || await database.Set<BudgetPlanMember>().AnyAsync(item => item.PlanId == plan.Id && item.UserId == userId, cancellationToken);

    public static async Task<PlanSnapshot> DescribeViewerAsync(ApplicationDbContext database, BudgetPlan plan, PlanSnapshot snapshot,
        string userId, CancellationToken cancellationToken)
        => string.Equals(plan.OwnerId, userId, StringComparison.Ordinal)
            ? snapshot with { IsOwner = true }
            : snapshot with { IsOwner = false, OwnerName = await OwnerNameAsync(database, plan.OwnerId, cancellationToken) };

    public static async Task<string?> OwnerNameAsync(ApplicationDbContext database, string ownerId, CancellationToken cancellationToken)
    {
        var owner = await database.Set<ApplicationUser>().AsNoTracking()
            .Where(item => item.Id == ownerId)
            .Select(item => new { item.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);
        return owner is null ? null : UserDisplayNames.Resolve(owner.DisplayName);
    }

    public static BudgetRequestException Unavailable() => new(404, "This plan is not available.");
}
