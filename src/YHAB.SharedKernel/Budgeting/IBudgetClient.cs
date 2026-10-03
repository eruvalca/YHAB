namespace YHAB.SharedKernel.Budgeting;

public interface IBudgetClient
{
    Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken = default);
    Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken = default);
}
