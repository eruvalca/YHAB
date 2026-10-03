using OneOf;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Models;

[GenerateOneOf]
internal sealed partial class BudgetChangeOutcome : OneOfBase<PlanSnapshot, InvalidBudgetChange, PlanNotFound, PlanVersionConflict>;
