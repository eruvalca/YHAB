namespace YHAB.Features.Budgeting.Services;

internal sealed class BudgetClock(TimeProvider timeProvider, IConfiguration configuration)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Budgeting:TimeZone"] ?? "America/Chicago");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _zone).DateTime);
}
