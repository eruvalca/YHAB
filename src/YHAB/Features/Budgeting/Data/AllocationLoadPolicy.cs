using Microsoft.EntityFrameworkCore;
using YHAB.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Data;

internal static class AllocationLoadPolicy
{
    public static Task<List<BudgetAllocation>> LoadAsync(ApplicationDbContext database, Guid planId, PlanCommand? command, CancellationToken token)
    {
        var allocations = database.Set<BudgetAllocation>().Where(item => item.PlanId == planId);
        return command switch
        {
            AssignMoney change => allocations.Where(item => item.CategoryId == change.CategoryId && item.Month == change.Month).ToListAsync(token),
            MoveMoney change => allocations.Where(item => item.Month == change.Month && (item.CategoryId == change.FromCategoryId || item.CategoryId == change.ToCategoryId)).ToListAsync(token),
            AutoAssign change => allocations.Where(item => item.Month == change.Month).ToListAsync(token),
            RemoveCategory change => allocations.Where(item => item.CategoryId == change.CategoryId || item.CategoryId == change.ReplacementCategoryId).ToListAsync(token),
            SaveGroup or SaveCategory or UpdatePlan or SaveAccount or SaveTransaction or UpdateTransactionStates
                or DeleteTransactions or ReconcileAccount or RenamePayee or PostRecurring => Task.FromResult(new List<BudgetAllocation>()),
            _ => allocations.ToListAsync(token),
        };
    }
}
