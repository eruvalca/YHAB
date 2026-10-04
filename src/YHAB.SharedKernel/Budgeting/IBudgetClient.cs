namespace YHAB.SharedKernel.Budgeting;

public interface IBudgetClient
{
    Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken = default);
    Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<PlanView> ReadViewAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<PlanMonthView> ReadWorkspaceAsync(Guid planId, DateOnly? month, CancellationToken cancellationToken = default);
    Task<long> ReadRevisionAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<BudgetMonth> ReadMonthAsync(Guid planId, DateOnly month, long version, CancellationToken cancellationToken = default);
    Task<RegisterPage> ReadRegisterAsync(Guid planId, RegisterQuery query, CancellationToken cancellationToken = default);
    Task<ReportView> ReadReportsAsync(Guid planId, DateOnly from, DateOnly through, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ReadPayeesAsync(Guid planId, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken = default);
}
