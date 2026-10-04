using System.Collections.Immutable;

namespace YHAB.SharedKernel.Budgeting;

/// <summary>Historical target contributions and future reservations, aggregated outside a monthly ledger window.</summary>
public sealed record BudgetMonthFunding(decimal FutureAssigned, ImmutableDictionary<Guid, decimal> PriorAssigned);
