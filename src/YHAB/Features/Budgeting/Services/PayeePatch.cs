using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

/// <summary>Only payee names changed by a rename; restoring it preserves every other transaction field.</summary>
internal sealed record PayeePatch(IReadOnlyList<TransactionPayee> Before, IReadOnlyList<TransactionPayee> After)
{
    public static string? ValidationError(RenamePayee command)
        => string.IsNullOrWhiteSpace(command.OldName) || string.IsNullOrWhiteSpace(command.NewName) || command.NewName.Length > 200
            ? "Enter an existing payee and a new name of at most 200 characters." : null;

    // The operation boundary validates the command before constructing the patch.
    public static PayeePatch Rename(IReadOnlyList<TransactionPayee> current, RenamePayee command)
    {
        var name = command.NewName.Trim();
        var before = current.Where(item => string.Equals(item.Payee, command.OldName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Payee, name, StringComparison.Ordinal)).ToArray();
        return new(before, before.Select(item => item with { Payee = name }).ToArray());
    }

    public PayeePatch Reverse() => this with { Before = After, After = Before };

    public bool CanApply(IReadOnlyList<TransactionPayee> current)
    {
        var actual = current.ToDictionary(item => item.Id);
        return Before.Count == After.Count
            && Before.Select(item => item.Id).ToHashSet().SetEquals(After.Select(item => item.Id))
            && Before.All(item => actual.TryGetValue(item.Id, out var value) && item == value);
    }
}
