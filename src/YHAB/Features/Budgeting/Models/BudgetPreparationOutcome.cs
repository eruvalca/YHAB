using OneOf;

namespace YHAB.Features.Budgeting.Models;

[GenerateOneOf]
internal sealed partial class BudgetPreparationOutcome : OneOfBase<PreparedBudgetChange, InvalidBudgetChange, PlanNotFound, PlanVersionConflict>;
