namespace YHAB.SharedKernel.Budgeting;

/// <summary>Workspace catalog and totals. Catalog deliberately contains no transactions.</summary>
public sealed record PlanView(PlanSnapshot Catalog, IReadOnlyList<AccountBalance> Balances, bool HasDueRecurring);
