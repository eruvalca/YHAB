using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using YHAB.Data;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class IdentityPersistenceTests
{
    [Fact]
    public async Task MigratedDatabasePersistsIdentityUsersAndEnforcesUniqueUserNames()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        // Match Aspire.Hosting.PostgreSQL 13.6.0's default image; review together on upgrades.
        await using var postgres = new PostgreSqlBuilder("postgres:18.3").Build();
        await postgres.StartAsync(timeout.Token);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();

        string userId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await database.Database.MigrateAsync(timeout.Token);
            (await database.Database.GetPendingMigrationsAsync(timeout.Token)).ShouldBeEmpty();
            (await database.Database.GetAppliedMigrationsAsync(timeout.Token)).ShouldNotBeEmpty();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "persist@example.test", Email = "persist@example.test" };
            (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();
            (await users.SetPhoneNumberAsync(user, "+15555550123")).Succeeded.ShouldBeTrue();
            userId = user.Id;
        }

        // A fresh scope/context proves the result was persisted, not merely tracked by EF.
        await using var verification = provider.CreateAsyncScope();
        var persistedUsers = verification.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await persistedUsers.FindByIdAsync(userId);
        persisted.ShouldNotBeNull();
        persisted.Email.ShouldBe("persist@example.test");
        persisted.PhoneNumber.ShouldBe("+15555550123");
        var duplicate = await persistedUsers.CreateAsync(new ApplicationUser { UserName = "PERSIST@EXAMPLE.TEST" });
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Errors.ShouldContain(error => string.Equals(error.Code, "DuplicateUserName", StringComparison.Ordinal));
    }
}
