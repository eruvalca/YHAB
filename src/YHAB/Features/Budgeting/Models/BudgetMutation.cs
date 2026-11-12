using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Models;

internal union BudgetMutation(LedgerPatch, PayeePatch);
