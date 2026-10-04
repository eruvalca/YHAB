namespace YHAB.SharedKernel.Budgeting;

/// <summary>Ledger movements before the report start and monthly totals within its inclusive date range. Opening account balances are supplied by the catalog.</summary>
public sealed record ReportTotals(IReadOnlyDictionary<Guid, decimal> PriorAccountChanges,
    IReadOnlyList<ReportAccountMonth> AccountMonths, IReadOnlyList<ReportCategoryMonth> CategoryMonths);
