using Microsoft.EntityFrameworkCore.Diagnostics;
using YHAB.Features.Budgeting.Data;

namespace YHAB.IntegrationTests;

internal sealed class PlanClaimBarrier(long version) : SaveChangesInterceptor
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;
    public bool Armed { get; set; }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Armed && eventData.Context!.ChangeTracker.Entries<BudgetPlan>().Any(entry => entry.Property(item => item.Version).OriginalValue == version && entry.Entity.Version == version + 1))
        {
            if (Interlocked.Increment(ref _arrivals) == 2) { _ready.TrySetResult(); }
            await _ready.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}
