namespace YHAB.SharedKernel.Budgeting;

/// <summary>Only the before/after records changed by one operation, independent of persistence and history cursors.</summary>
public sealed record LedgerPatch(PlanSnapshot Before, PlanSnapshot After)
{
    public bool IsEmpty => string.Equals(Before.Name, After.Name, StringComparison.Ordinal) && string.Equals(Before.Notes, After.Notes, StringComparison.Ordinal)
        && Before.Accounts.Count + After.Accounts.Count + Before.Groups.Count + After.Groups.Count
        + Before.Categories.Count + After.Categories.Count + Before.Allocations.Count + After.Allocations.Count
        + Before.Transactions.Count + After.Transactions.Count == 0;

    public static LedgerPatch Between(PlanSnapshot before, PlanSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return new(Difference(before, after), Difference(after, before));
    }

    public bool TryApply(PlanSnapshot current, bool reverse, out PlanSnapshot result)
    {
        ArgumentNullException.ThrowIfNull(current);
        var expected = reverse ? After : Before;
        var desired = reverse ? Before : After;
        result = current;
        if (current.Id != expected.Id
            || (!string.Equals(expected.Name, desired.Name, StringComparison.Ordinal) && !string.Equals(current.Name, expected.Name, StringComparison.Ordinal))
            || (!string.Equals(expected.Notes, desired.Notes, StringComparison.Ordinal) && !string.Equals(current.Notes, expected.Notes, StringComparison.Ordinal))
            || !CanApply(current.Accounts, expected.Accounts, desired.Accounts, item => item.Id)
            || !CanApply(current.Groups, expected.Groups, desired.Groups, item => item.Id)
            || !CanApply(current.Categories, expected.Categories, desired.Categories, item => item.Id)
            || !CanApply(current.Allocations, expected.Allocations, desired.Allocations, item => (item.CategoryId, item.Month))
            || !CanApply(current.Transactions, expected.Transactions, desired.Transactions, item => item.Id))
        {
            return false;
        }
        var candidate = current with
        {
            Name = string.Equals(expected.Name, desired.Name, StringComparison.Ordinal) ? current.Name : desired.Name,
            Notes = string.Equals(expected.Notes, desired.Notes, StringComparison.Ordinal) ? current.Notes : desired.Notes,
            Accounts = Apply(current.Accounts, expected.Accounts, desired.Accounts, item => item.Id),
            Groups = Apply(current.Groups, expected.Groups, desired.Groups, item => item.Id),
            Categories = Apply(current.Categories, expected.Categories, desired.Categories, item => item.Id),
            Allocations = Apply(current.Allocations, expected.Allocations, desired.Allocations, item => (item.CategoryId, item.Month)),
            Transactions = Apply(current.Transactions, expected.Transactions, desired.Transactions, item => item.Id),
        };
        if (!ReferencesExist(candidate))
        {
            return false;
        }
        result = candidate;
        return true;
    }

    private static PlanSnapshot Difference(PlanSnapshot source, PlanSnapshot other) => source with
    {
        Accounts = Changed(source.Accounts, other.Accounts, item => item.Id),
        Groups = Changed(source.Groups, other.Groups, item => item.Id),
        Categories = Changed(source.Categories, other.Categories, item => item.Id),
        Allocations = Changed(source.Allocations, other.Allocations, item => (item.CategoryId, item.Month)),
        Transactions = Changed(source.Transactions, other.Transactions, item => item.Id),
        Changes = [],
        CanUndo = false,
        CanRedo = false,
    };

    private static T[] Changed<T, TKey>(IReadOnlyList<T> source, IReadOnlyList<T> other, Func<T, TKey> key) where TKey : notnull
    {
        var lookup = other.ToDictionary(key);
        return source.Where(item => !lookup.TryGetValue(key(item), out var value) || !Equal(item, value)).ToArray();
    }

    private static bool CanApply<T, TKey>(IReadOnlyList<T> current, IReadOnlyList<T> expected, IReadOnlyList<T> desired, Func<T, TKey> key) where TKey : notnull
    {
        var actual = current.ToDictionary(key);
        var previous = expected.ToDictionary(key);
        return expected.All(item => actual.TryGetValue(key(item), out var value) && Equal(item, value))
            && desired.All(item => previous.ContainsKey(key(item)) || !actual.ContainsKey(key(item)));
    }

    private static T[] Apply<T, TKey>(IReadOnlyList<T> current, IReadOnlyList<T> expected, IReadOnlyList<T> desired, Func<T, TKey> key) where TKey : notnull
    {
        var replaced = expected.Select(key).Concat(desired.Select(key)).ToHashSet();
        return current.Where(item => !replaced.Contains(key(item))).Concat(desired).ToArray();
    }

    private static bool Equal<T>(T left, T right)
        => left is TransactionData a && right is TransactionData b
            ? a with { Splits = b.Splits } == b && a.Splits.OrderBy(item => item.Id).SequenceEqual(b.Splits.OrderBy(item => item.Id))
            : EqualityComparer<T>.Default.Equals(left, right);

    private static bool ReferencesExist(PlanSnapshot plan)
    {
        var accounts = plan.Accounts.Select(item => item.Id).ToHashSet();
        var groups = plan.Groups.Select(item => item.Id).ToHashSet();
        var categories = plan.Categories.Select(item => item.Id).ToHashSet();
        return plan.Categories.All(item => groups.Contains(item.GroupId) && (!item.CreditAccountId.HasValue || accounts.Contains(item.CreditAccountId.Value)))
            && plan.Allocations.All(item => categories.Contains(item.CategoryId))
            && plan.Transactions.All(item => accounts.Contains(item.AccountId)
                && (!item.TransferAccountId.HasValue || accounts.Contains(item.TransferAccountId.Value))
                && item.Splits.All(split => !split.CategoryId.HasValue || categories.Contains(split.CategoryId.Value)));
    }
}
