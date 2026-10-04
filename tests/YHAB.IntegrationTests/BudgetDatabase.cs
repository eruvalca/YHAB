using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using YHAB.Data;
using YHAB.Features.Budgeting.Services;

namespace YHAB.IntegrationTests;

internal sealed class BudgetDatabase(PostgreSqlContainer container, ServiceProvider provider) : IAsyncDisposable
{
    public IDbContextFactory<ApplicationDbContext> Factory => provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    public BudgetStore Store => provider.GetRequiredService<BudgetStore>();
    public BudgetQueries Queries => provider.GetRequiredService<BudgetQueries>();
    public RecurringProcessor Recurring => provider.GetRequiredService<RecurringProcessor>();
    // Separate service graphs model replicas sharing only the disposable database.
    public ServiceProvider CreatePeerServices(ILoggerProvider logger)
        => BuildServices(container.GetConnectionString(), provider.GetRequiredService<TimeProvider>(), null, logger);
    public static async Task<BudgetDatabase> CreateAsync(CancellationToken cancellationToken, TimeProvider? timeProvider = null, IInterceptor? interceptor = null)
    {
        var container = new PostgreSqlBuilder("postgres:18.3").Build();
        ServiceProvider? provider = null;
        try
        {
            await container.StartAsync(cancellationToken);
            provider = BuildServices(container.GetConnectionString(), timeProvider ?? new FixedTime(), interceptor);
            var result = new BudgetDatabase(container, provider);
            await using var context = await result.Factory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            context.Users.AddRange(new ApplicationUser { Id = "owner-a", UserName = "a@example.test", NormalizedUserName = "A@EXAMPLE.TEST" },
                new ApplicationUser { Id = "owner-b", UserName = "b@example.test", NormalizedUserName = "B@EXAMPLE.TEST" });
            await context.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (provider is not null) { await provider.DisposeAsync(); }
            await container.DisposeAsync();
            throw;
        }
    }

    private static ServiceProvider BuildServices(string connection, TimeProvider timeProvider, IInterceptor? interceptor, ILoggerProvider? logger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (logger is not null) { services.AddLogging(builder => builder.AddProvider(logger)); }
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(timeProvider);
        services.AddSingleton<BudgetClock>();
        services.AddTransient<BudgetStore>();
        services.AddSingleton<BudgetMonthCache>();
        services.AddTransient<BudgetQueries>();
        services.AddTransient<RecurringProcessor>();
        // Measurement interceptors own mutable per-fixture state. Keep EF's
        // singleton services in this disposable graph instead of its global cache.
        services.AddEntityFrameworkNpgsql();
        if (interceptor is not null) { services.AddSingleton(interceptor); }
        if (interceptor is ISingletonInterceptor singleton) { services.AddSingleton(singleton); }
        services.AddDbContextFactory<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseInternalServiceProvider(serviceProvider);
            options.UseNpgsql(connection, postgres => postgres.EnableRetryOnFailure());
        });
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3).AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }
    public async ValueTask DisposeAsync() { await provider.DisposeAsync(); await container.DisposeAsync(); }
    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    }
}
