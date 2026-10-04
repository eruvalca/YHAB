namespace YHAB.SharedKernel.Budgeting;

public interface IBudgetClient
{
    Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken);
    Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken);
    Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken);
    Task<PlanView> ReadViewAsync(Guid planId, CancellationToken cancellationToken);
    Task<PlanMonthView> ReadWorkspaceAsync(Guid planId, DateOnly? month, CancellationToken cancellationToken);
    Task<long> ReadRevisionAsync(Guid planId, CancellationToken cancellationToken);
    Task<BudgetMonth> ReadMonthAsync(Guid planId, DateOnly month, long version, CancellationToken cancellationToken);
    Task<RegisterPage> ReadRegisterAsync(Guid planId, RegisterQuery query, CancellationToken cancellationToken);
    Task<ReportView> ReadReportsAsync(Guid planId, DateOnly from, DateOnly through, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadPayeesAsync(Guid planId, CancellationToken cancellationToken);
    Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken);
}
