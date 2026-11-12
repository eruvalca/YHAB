using Microsoft.Extensions.Caching.Memory;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class BudgetMonthCache : IDisposable
{
    // Statistics feed the .NET 11 MemoryCache meter; the instruments stay dormant unless requested.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 100_000, TrackStatistics = true });

    public bool TryGet((string Owner, Guid Plan, long Version, DateOnly Today, DateOnly Month) key, out BudgetMonth value)
        => _cache.TryGetValue(key, out value!);

    public void Set((string Owner, Guid Plan, long Version, DateOnly Today, DateOnly Month) key, BudgetMonth value)
        => _cache.Set(key, value, new MemoryCacheEntryOptions().SetSize(Math.Max(1, value.Categories.Count)).SetAbsoluteExpiration(TimeSpan.FromMinutes(2)));

    public void Dispose() => _cache.Dispose();
}
