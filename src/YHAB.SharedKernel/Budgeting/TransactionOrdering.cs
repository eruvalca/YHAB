namespace YHAB.SharedKernel.Budgeting;

/// <summary>Stable creation order within a financial date; identifiers are not timestamps or sequence numbers.</summary>
public static class TransactionOrdering
{
    public static (IReadOnlyList<TransactionData> Transactions, long LastSequence) Assign(
        IReadOnlyList<TransactionData> before, IReadOnlyList<TransactionData> after, long lastSequence, bool restoring)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var existing = before.ToDictionary(item => item.Id, item => item.Sequence);
        lastSequence = Math.Max(lastSequence, before.Select(item => item.Sequence).DefaultIfEmpty().Max());
        var result = new List<TransactionData>(after.Count);
        foreach (var entry in after)
        {
            if (!existing.TryGetValue(entry.Id, out var sequence))
            {
                sequence = restoring ? entry.Sequence : checked(++lastSequence);
                lastSequence = Math.Max(lastSequence, sequence);
            }
            result.Add(entry with { Sequence = sequence });
        }
        return (result, lastSequence);
    }
}
