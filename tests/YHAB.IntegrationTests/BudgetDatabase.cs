using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using YHAB.Data;
using YHAB.Features.Budgeting.Services;

namespace YHAB.IntegrationTests;

internal sealed class BudgetDatabase(PostgreSqlContainer container, ServiceProvider provider) : IAsyncDisposable
{
    public IDbContextFactory<ApplicationDbContext> Factory => provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    public BudgetStore Store => provider.GetRequiredService<BudgetStore>();
    public static async Task<BudgetDatabase> CreateAsync(CancellationToken cancellationToken)
    {
        var container = new PostgreSqlBuilder("postgres:18.3").Build();
        ServiceProvider? provider = null;
        try
        {
            await container.StartAsync(cancellationToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<TimeProvider, FixedTime>();
            services.AddSingleton<BudgetClock>();
            services.AddTransient<BudgetStore>();
            services.AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(container.GetConnectionString()));
            services.AddIdentityCore<ApplicationUser>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3).AddEntityFrameworkStores<ApplicationDbContext>();
            provider = services.BuildServiceProvider();
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
    public async ValueTask DisposeAsync() { await provider.DisposeAsync(); await container.DisposeAsync(); }
    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    }
}

