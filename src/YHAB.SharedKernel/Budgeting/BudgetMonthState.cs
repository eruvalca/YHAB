using System.Collections.Immutable;

namespace YHAB.SharedKernel.Budgeting;

/// <summary>Opening balances and envelope carry for one month. Contains no transaction history.</summary>
public sealed record BudgetMonthState(DateOnly Month, ImmutableDictionary<Guid, decimal> Carry, ImmutableDictionary<Guid, decimal> Balances);
