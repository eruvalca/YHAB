namespace YHAB.Features.Budgeting.Models;

internal union BudgetPreparationOutcome(PreparedBudgetChange, InvalidBudgetChange, PlanNotFound, PlanVersionConflict);
