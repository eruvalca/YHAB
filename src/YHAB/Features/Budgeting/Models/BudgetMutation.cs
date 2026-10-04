using OneOf;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Models;

[GenerateOneOf]
internal sealed partial class BudgetMutation : OneOfBase<LedgerPatch, PayeePatch>
{
    public bool IsEmpty => Match(ledger => ledger.IsEmpty, payees => payees.Before.Count == 0);
}
