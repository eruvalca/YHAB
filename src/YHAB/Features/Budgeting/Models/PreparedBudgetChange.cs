using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Models;

internal sealed record PreparedBudgetChange(PlanSnapshot Snapshot, BudgetMutation Mutation);
