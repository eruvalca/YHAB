using System.Text.Json;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal static class BudgetHistoryCodec
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private const string PayeeKind = "payee-names-v1";

    public static (string Before, string After) Serialize(BudgetMutation mutation)
        => mutation.Match(ledger => (JsonSerializer.Serialize(ledger.Before, _json), JsonSerializer.Serialize(ledger.After, _json)),
            payees => (JsonSerializer.Serialize(new PayeeHistory(PayeeKind, payees.Before), _json),
                JsonSerializer.Serialize(new PayeeHistory(PayeeKind, payees.After), _json)));

    public static BudgetMutation Deserialize(BudgetHistory history)
    {
        using var before = JsonDocument.Parse(history.Before);
        using var after = JsonDocument.Parse(history.After);
        if (before.RootElement.ValueKind != JsonValueKind.Object || after.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw Unreadable();
        }
        if (before.RootElement.TryGetProperty("kind", out var kind))
        {
            if (!string.Equals(kind.GetString(), PayeeKind, StringComparison.Ordinal)
                || !after.RootElement.TryGetProperty("kind", out var afterKind)
                || !string.Equals(afterKind.GetString(), PayeeKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The saved plan history format is not supported.");
            }
            // Object roots with the default serializer produce an instance or
            // throw. The values property can still be absent or explicitly null.
            var oldNames = before.RootElement.Deserialize<PayeeHistory>(_json)!.Values;
            var newNames = after.RootElement.Deserialize<PayeeHistory>(_json)!.Values;
            return new PayeePatch(oldNames ?? throw Unreadable(), newNames ?? throw Unreadable());
        }
        if (after.RootElement.TryGetProperty("kind", out _)) { throw Unreadable(); }
        // Existing ledger history remains in its original snapshot representation.
        var oldState = before.RootElement.Deserialize<PlanSnapshot>(_json)!;
        var newState = after.RootElement.Deserialize<PlanSnapshot>(_json)!;
        return LedgerPatch.Between(oldState, newState);
    }

    private static InvalidOperationException Unreadable() => new("The saved plan history could not be read.");
    private sealed record PayeeHistory(string Kind, IReadOnlyList<TransactionPayee> Values);
}
