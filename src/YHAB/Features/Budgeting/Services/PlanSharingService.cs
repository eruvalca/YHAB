using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class PlanSharingService(IDbContextFactory<ApplicationDbContext> factory, ILookupNormalizer normalizer)
{
    public async Task<PlanShareSummary> ReadAsync(string userId, Guid planId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (!await PlanAccess.CanAccessAsync(database, plan, userId, cancellationToken))
        {
            throw PlanAccess.Unavailable();
        }
        return await SummarizeAsync(database, plan, userId, cancellationToken);
    }

    public async Task<PlanShareSummary> ShareAsync(string userId, Guid planId, string email, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (!string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
        {
            throw PlanAccess.Unavailable();
        }

        var address = email?.Trim() ?? string.Empty;
        if (address.Length is 0 or > 256)
        {
            throw new BudgetRequestException(400, "Enter the email address of another YHAB account.");
        }
        var target = await database.Set<ApplicationUser>()
            .SingleOrDefaultAsync(item => item.NormalizedUserName == normalizer.NormalizeName(address), cancellationToken)
            ?? throw new BudgetRequestException(404, "No YHAB account uses that email address.");
        if (string.Equals(target.Id, userId, StringComparison.Ordinal))
        {
            throw new BudgetRequestException(400, "You already own this plan.");
        }
        if (await database.Set<BudgetPlanMember>().AnyAsync(item => item.PlanId == planId && item.UserId == target.Id, cancellationToken))
        {
            throw new BudgetRequestException(409, "That account can already open this plan.");
        }

        database.Add(new BudgetPlanMember { PlanId = planId, UserId = target.Id });
        await database.SaveChangesAsync(cancellationToken);
        return await SummarizeAsync(database, plan, userId, cancellationToken);
    }

    public async Task<PlanShareSummary> UnshareAsync(string userId, Guid planId, string memberId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (!string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
        {
            throw PlanAccess.Unavailable();
        }

        var member = await database.Set<BudgetPlanMember>()
            .SingleOrDefaultAsync(item => item.PlanId == planId && item.UserId == memberId, cancellationToken)
            ?? throw new BudgetRequestException(404, "That account does not have access to this plan.");
        database.Remove(member);
        await database.SaveChangesAsync(cancellationToken);
        return await SummarizeAsync(database, plan, userId, cancellationToken);
    }

    public async Task LeaveAsync(string userId, Guid planId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
        {
            throw new BudgetRequestException(400, "You cannot leave a plan you own.");
        }
        var member = await database.Set<BudgetPlanMember>()
            .SingleOrDefaultAsync(item => item.PlanId == planId && item.UserId == userId, cancellationToken)
            ?? throw PlanAccess.Unavailable();
        database.Remove(member);
        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task<BudgetPlan> PlanAsync(ApplicationDbContext database, Guid planId, CancellationToken cancellationToken)
        => await database.Set<BudgetPlan>().SingleOrDefaultAsync(item => item.Id == planId, cancellationToken)
            ?? throw PlanAccess.Unavailable();

    private static async Task<PlanShareSummary> SummarizeAsync(ApplicationDbContext database, BudgetPlan plan, string userId, CancellationToken cancellationToken)
    {
        var owner = await PlanAccess.OwnerNameAsync(database, plan.OwnerId, cancellationToken) ?? "the plan owner";
        var collaborators = await database.Set<BudgetPlanMember>().AsNoTracking()
            .Where(item => item.PlanId == plan.Id)
            .Join(database.Set<ApplicationUser>(), member => member.UserId, user => user.Id,
                (member, user) => new { user.Id, Name = user.UserName ?? user.Id })
            .OrderBy(item => item.Name)
            .Select(item => new PlanCollaborator(item.Id, item.Name))
            .ToArrayAsync(cancellationToken);
        return new(owner, string.Equals(plan.OwnerId, userId, StringComparison.Ordinal), collaborators);
    }
}
