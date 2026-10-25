using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class PlanSharingService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider timeProvider)
{
    private static readonly TimeSpan _codeLifetime = TimeSpan.FromDays(7);
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

    public async Task<PlanShareCode> CreateShareCodeAsync(string userId, Guid planId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (!string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
        {
            throw PlanAccess.Unavailable();
        }

        var now = timeProvider.GetUtcNow();
        await database.Set<BudgetPlanShareCode>()
            .Where(item => item.PlanId == planId && item.ExpiresOn <= now)
            .ExecuteDeleteAsync(cancellationToken);
        var code = CreateCode();
        var expiresOn = now.Add(_codeLifetime);
        database.Add(new BudgetPlanShareCode
        {
            Id = Guid.CreateVersion7(),
            PlanId = planId,
            CodeHash = Hash(code),
            ExpiresOn = expiresOn,
        });
        await database.SaveChangesAsync(cancellationToken);
        return new(planId, Format(code), expiresOn);
    }

    public async Task<Guid> JoinAsync(string userId, string code, CancellationToken cancellationToken)
    {
        var normalized = Normalize(code);
        if (normalized.Length == 0)
        {
            throw new BudgetRequestException(400, "Enter a share code.");
        }
        var hash = Hash(normalized);
        await using var strategyContext = await factory.CreateDbContextAsync(cancellationToken);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            await using var database = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            // A retry after a committed redemption, including one whose acknowledgement was
            // lost to a transient failure, must return the original result even though the
            // code row is gone.
            var redeemed = await database.Set<BudgetPlanShareCodeRedemption>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.CodeHash == hash && item.UserId == userId, cancellationToken);
            if (redeemed is not null)
            {
                return await IsMemberAsync(database, redeemed.PlanId, userId, cancellationToken)
                    ? redeemed.PlanId
                    : throw InvalidCode();
            }
            var invitation = await database.Set<BudgetPlanShareCode>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.CodeHash == hash && item.ExpiresOn > now, cancellationToken);
            if (invitation is null)
            {
                // The same account may have committed its redemption between our receipt
                // lookup and this invitation lookup.
                return await ReceiptPlanAsync(database, hash, userId, cancellationToken);
            }
            var plan = await database.Set<BudgetPlan>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == invitation.PlanId, cancellationToken)
                ?? throw InvalidCode();
            if (string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
            {
                throw new BudgetRequestException(400, "You already own this plan.");
            }
            // Consume the code atomically so a second redemption cannot create a second membership.
            var consumed = await database.Set<BudgetPlanShareCode>()
                .Where(item => item.Id == invitation.Id && item.ExpiresOn > now)
                .ExecuteDeleteAsync(cancellationToken);
            if (consumed == 0)
            {
                // A concurrent redemption by this account may have committed first.
                return await ReceiptPlanAsync(database, hash, userId, cancellationToken);
            }
            // Membership is idempotent, so a later code for the same plan remains usable.
            await database.Database.ExecuteSqlAsync(
                $"INSERT INTO \"BudgetPlanMembers\" (\"PlanId\", \"UserId\") VALUES ({plan.Id}, {userId}) ON CONFLICT DO NOTHING",
                cancellationToken);
            database.Add(new BudgetPlanShareCodeRedemption
            {
                CodeHash = hash,
                UserId = userId,
                PlanId = plan.Id,
                RedeemedOn = now,
            });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return plan.Id;
        }, cancellationToken);
    }

    public async Task<PlanShareSummary> UnshareAsync(string userId, Guid planId, string memberId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (!string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
        {
            throw PlanAccess.Unavailable();
        }

        var removed = await database.Set<BudgetPlanMember>()
            .Where(item => item.PlanId == planId && item.UserId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            throw new BudgetRequestException(404, "That account does not have access to this plan.");
        }
        return await SummarizeAsync(database, plan, userId, cancellationToken);
    }

    public async Task<PlanShareSummary> TransferOwnershipAsync(string userId, Guid planId, string memberId, CancellationToken cancellationToken)
    {
        await using var strategyContext = await factory.CreateDbContextAsync(cancellationToken);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            await using var database = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            // Lock the plan row so two owners cannot hand the same plan to different accounts.
            var plan = await database.Set<BudgetPlan>().FromSql($"""
                SELECT * FROM "BudgetPlans" WHERE "Id" = {planId} FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
            if (plan is null || !string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
            {
                throw PlanAccess.Unavailable();
            }
            if (string.IsNullOrWhiteSpace(memberId))
            {
                throw new BudgetRequestException(400, "Choose a collaborator to take over this plan.");
            }
            var member = await database.Set<BudgetPlanMember>()
                .SingleOrDefaultAsync(item => item.PlanId == planId && item.UserId == memberId, cancellationToken)
                ?? throw new BudgetRequestException(404, "That account does not have access to this plan.");
            // The outgoing owner keeps editing access as an ordinary collaborator.
            var previousOwnerIsMember = await database.Set<BudgetPlanMember>()
                .AnyAsync(item => item.PlanId == planId && item.UserId == userId, cancellationToken);
            database.Remove(member);
            if (!previousOwnerIsMember)
            {
                database.Add(new BudgetPlanMember { PlanId = planId, UserId = userId });
            }

            plan.OwnerId = memberId;
            await database.SaveChangesAsync(cancellationToken);
            // Read the summary before committing so a transient read failure retries the whole
            // uncommitted operation instead of replaying it after the owner already changed.
            var summary = await SummarizeAsync(database, plan, userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return summary;
        }, cancellationToken);
    }

    public async Task DeleteAsync(string userId, Guid planId, string planName, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (!string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
        {
            throw PlanAccess.Unavailable();
        }
        var confirmedName = planName?.Trim() ?? string.Empty;
        if (!string.Equals(plan.Name, confirmedName, StringComparison.Ordinal))
        {
            throw new BudgetRequestException(400, "Type the plan name to confirm deletion.");
        }
        // Repeat the checked owner and name in the delete predicate so a transfer or rename
        // that lands after this read cannot delete the plan on stale authorization.
        var deleted = await database.Set<BudgetPlan>()
            .Where(item => item.Id == planId && item.OwnerId == userId && item.Name == confirmedName)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            var stillExists = await database.Set<BudgetPlan>().AnyAsync(item => item.Id == planId, cancellationToken);
            throw stillExists
                ? new BudgetRequestException(409, "This plan changed. Reload and confirm deletion again.")
                : PlanAccess.Unavailable();
        }
    }

    public async Task LeaveAsync(string userId, Guid planId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var plan = await PlanAsync(database, planId, cancellationToken);
        if (string.Equals(plan.OwnerId, userId, StringComparison.Ordinal))
        {
            throw new BudgetRequestException(400, "You cannot leave a plan you own.");
        }
        var removed = await database.Set<BudgetPlanMember>()
            .Where(item => item.PlanId == planId && item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            throw PlanAccess.Unavailable();
        }
    }

    private static BudgetRequestException InvalidCode() => new(404, "That share code is invalid or expired.");

    private static Task<bool> IsMemberAsync(ApplicationDbContext database, Guid planId, string userId, CancellationToken cancellationToken)
        => database.Set<BudgetPlanMember>().AnyAsync(item => item.PlanId == planId && item.UserId == userId, cancellationToken);

    private static async Task<Guid> ReceiptPlanAsync(ApplicationDbContext database, string codeHash, string userId, CancellationToken cancellationToken)
    {
        var receipt = await database.Set<BudgetPlanShareCodeRedemption>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CodeHash == codeHash && item.UserId == userId, cancellationToken);
        if (receipt is null || !await IsMemberAsync(database, receipt.PlanId, userId, cancellationToken))
        {
            throw InvalidCode();
        }
        return receipt.PlanId;
    }

    private static string CreateCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(10));

    private static string Format(string code)
        => string.Join('-', Enumerable.Range(0, 4).Select(index => code.Substring(index * 5, 5)));

    private static string Normalize(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : string.Concat(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant));

    private static string Hash(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static async Task<BudgetPlan> PlanAsync(ApplicationDbContext database, Guid planId, CancellationToken cancellationToken)
        => await database.Set<BudgetPlan>().SingleOrDefaultAsync(item => item.Id == planId, cancellationToken)
            ?? throw PlanAccess.Unavailable();

    private static async Task<PlanShareSummary> SummarizeAsync(ApplicationDbContext database, BudgetPlan plan, string userId, CancellationToken cancellationToken)
    {
        var owner = await PlanAccess.OwnerNameAsync(database, plan.OwnerId, cancellationToken) ?? "the plan owner";
        var collaborators = await database.Set<BudgetPlanMember>().AsNoTracking()
            .Where(item => item.PlanId == plan.Id)
            .Join(database.Set<ApplicationUser>(), member => member.UserId, user => user.Id,
                (member, user) => new { user.Id, user.DisplayName, user.Email, user.UserName })
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return new(owner, string.Equals(plan.OwnerId, userId, StringComparison.Ordinal),
            collaborators.Select(item => new PlanCollaborator(item.Id, UserDisplayNames.Resolve(item.DisplayName, item.Email, item.UserName))).ToArray());
    }
}
