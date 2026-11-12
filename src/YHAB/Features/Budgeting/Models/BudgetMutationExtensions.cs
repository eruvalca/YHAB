using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Models;

internal static class BudgetMutationExtensions
{
    extension(BudgetMutation mutation)
    {
        // Declared outside the union body: a type-pattern dispatch inside the union
        // trips S3060, and the union itself carries no behavior.
        internal bool IsEmpty => mutation switch
        {
            LedgerPatch ledger => ledger.IsEmpty,
            PayeePatch payees => payees.Before.Count == 0,
            null => false,
        };
    }
}
