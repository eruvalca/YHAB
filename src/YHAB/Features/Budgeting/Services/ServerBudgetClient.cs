using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class ServerBudgetClient(BudgetStore store, AuthenticationStateProvider authentication) : IBudgetClient
{
    public async Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken = default)
        => await store.ListAsync(await OwnerAsync(), cancellationToken);

    public async Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
        => await store.CreateAsync(await OwnerAsync(), request, cancellationToken);

    public async Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken = default)
        => await store.ReadAsync(await OwnerAsync(), planId, cancellationToken)
            ?? throw new BudgetRequestException(404, "This plan is not available.");

    public async Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken = default)
    {
        var result = await store.ExecuteAsync(await OwnerAsync(), planId, command, cancellationToken);
        result.Switch(
            _ => { },
            invalid => throw new BudgetRequestException(400, invalid.Message),
            _ => throw new BudgetRequestException(404, "This plan is not available."),
            _ => throw new BudgetRequestException(409, "This plan changed in another tab. Refresh and try your change again."));
    }

    private async Task<string> OwnerAsync()
    {
        var state = await authentication.GetAuthenticationStateAsync();
        return state.User.Identity?.IsAuthenticated == true
            ? state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new BudgetRequestException(401, "Please sign in again.")
            : throw new BudgetRequestException(401, "Please sign in to open your plans.");
    }
}
