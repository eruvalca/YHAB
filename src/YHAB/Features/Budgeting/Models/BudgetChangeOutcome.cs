using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Models;

internal union BudgetChangeOutcome(PlanSnapshot, InvalidBudgetChange, PlanNotFound, PlanVersionConflict);
