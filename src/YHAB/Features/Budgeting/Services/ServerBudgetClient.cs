using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Services;

internal sealed class ServerBudgetClient(BudgetStore store, BudgetQueries queries, AuthenticationStateProvider authentication) : IBudgetClient
{
    public async Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken)
        => await store.ListAsync(await OwnerAsync(cancellationToken), cancellationToken);

    public async Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken)
        => await store.CreateAsync(await OwnerAsync(cancellationToken), request, cancellationToken);

    public async Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken)
        => await store.ReadAsync(await OwnerAsync(cancellationToken), planId, cancellationToken)
            ?? throw new BudgetRequestException(404, "This plan is not available.");

    public async Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken)
    {
        var result = await store.ExecuteAsync(await OwnerAsync(cancellationToken), planId, command, cancellationToken);
        result.Switch(
            _ => { },
            invalid => throw new BudgetRequestException(400, invalid.Message),
            _ => throw new BudgetRequestException(404, "This plan is not available."),
            _ => throw new BudgetRequestException(409, "This plan changed in another tab. Refresh and try your change again."));
    }

    public async Task<PlanView> ReadViewAsync(Guid planId, CancellationToken cancellationToken)
        => await queries.ViewAsync(await OwnerAsync(cancellationToken), planId, cancellationToken);

    public async Task<PlanMonthView> ReadWorkspaceAsync(Guid planId, DateOnly? month, CancellationToken cancellationToken)
        => await queries.WorkspaceAsync(await OwnerAsync(cancellationToken), planId, month, cancellationToken);

    public async Task<long> ReadRevisionAsync(Guid planId, CancellationToken cancellationToken)
        => await queries.RevisionAsync(await OwnerAsync(cancellationToken), planId, cancellationToken);

    public async Task<BudgetMonth> ReadMonthAsync(Guid planId, DateOnly month, long version, CancellationToken cancellationToken)
        => await queries.MonthAsync(await OwnerAsync(cancellationToken), planId, month, version, cancellationToken);

    public async Task<RegisterPage> ReadRegisterAsync(Guid planId, RegisterQuery query, CancellationToken cancellationToken)
        => await queries.RegisterAsync(await OwnerAsync(cancellationToken), planId, query, cancellationToken);

    public async Task<ReportView> ReadReportsAsync(Guid planId, DateOnly from, DateOnly through, CancellationToken cancellationToken)
        => await queries.ReportsAsync(await OwnerAsync(cancellationToken), planId, from, through, cancellationToken);

    public async Task<IReadOnlyList<string>> ReadPayeesAsync(Guid planId, CancellationToken cancellationToken)
        => await queries.PayeesAsync(await OwnerAsync(cancellationToken), planId, cancellationToken);

    private async Task<string> OwnerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authentication.GetAuthenticationStateAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return state.User.Identity?.IsAuthenticated == true
            ? state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new BudgetRequestException(401, "Please sign in again.")
            : throw new BudgetRequestException(401, "Please sign in to open your plans.");
    }
}
