using YHAB.Features.Budgeting.Models;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Tests.Scenarios;

// Case inspection for tests. The index suffixes follow each union's declaration order: the first
// BudgetChangeOutcome case is PlanSnapshot, then InvalidBudgetChange, PlanNotFound and
// PlanVersionConflict; the first BudgetMutation case is LedgerPatch, then PayeePatch.
internal static class BudgetOutcomeCaseExtensions
{
    extension(BudgetChangeOutcome outcome)
    {
        internal bool IsT0 => outcome.Value is PlanSnapshot;

        internal PlanSnapshot AsT0 => (PlanSnapshot)outcome.Value;

        internal bool IsT1 => outcome.Value is InvalidBudgetChange;

        internal InvalidBudgetChange AsT1 => (InvalidBudgetChange)outcome.Value;

        internal bool IsT2 => outcome.Value is PlanNotFound;

        internal bool IsT3 => outcome.Value is PlanVersionConflict;
    }

    extension(BudgetMutation mutation)
    {
        internal bool IsT0 => mutation.Value is LedgerPatch;

        internal LedgerPatch AsT0 => (LedgerPatch)mutation.Value;

        internal bool IsT1 => mutation.Value is PayeePatch;

        internal PayeePatch AsT1 => (PayeePatch)mutation.Value;
    }
}
