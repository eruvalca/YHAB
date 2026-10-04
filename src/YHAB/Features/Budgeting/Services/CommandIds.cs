using System.Collections.Immutable;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

/// <summary>Immutable identities allocated by the operation boundary, before transforming a snapshot.</summary>
internal sealed record CommandIds(ImmutableArray<Guid> Values)
{
    public Guid this[int index] => Values[index];
    public CommandIds Skip(int count) => new(Values[count..]);

    // Reserve a bounded upper limit; unused IDs are harmless. No ledger transformation
    // calls this factory, reads a clock, or consumes a shared mutable ID generator.
    public static CommandIds Allocate(PlanSnapshot plan, PlanCommand command)
    {
        var count = command switch
        {
            SaveAccount => 3,
            SaveGroup or SaveCategory or ReconcileAccount => 1,
            SaveTransaction save => 1 + Math.Min(100, save.Transaction?.Splits?.Count ?? 0),
            _ => 0,
        };
        if (command is PostRecurring or SaveTransaction { Transaction.Repeat: not RepeatFrequency.None })
        {
            var maxSplits = plan.Transactions.Where(item => item.Repeat != RepeatFrequency.None)
                .Select(item => item.Splits.Count).DefaultIfEmpty(0).Max();
            if (command is SaveTransaction save) { maxSplits = Math.Max(maxSplits, Math.Min(100, save.Transaction.Splits?.Count ?? 0)); }
            count += RecurrencePlanner.BatchSize * (1 + maxSplits);
        }
        return new(Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToImmutableArray());
    }
}
