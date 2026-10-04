using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Models;
using YHAB.Features.Account.Services;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class IdentityPersistenceTests
{
    [Fact]
    public async Task OwnerRecoveryResetsUnconfirmedAccountOnceWithoutChangingTwoFactorAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await using var postgres = new PostgreSqlBuilder("postgres:18.3").Build();
        await postgres.StartAsync(timeout.Token);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddScoped<IdentityCancellation>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddUserManager<CancellableUserManager>()
            .AddDefaultTokenProviders();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IdentityCancellation>().Token = timeout.Token;
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.Database.MigrateAsync(timeout.Token);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "recovery@example.test", Email = "recovery@example.test", TwoFactorEnabled = true };
        (await users.CreateAsync(user, "Original-password!37")).Succeeded.ShouldBeTrue();
        var stamp = user.SecurityStamp;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        (await AccountRecoveryCommand.ExecuteAsync(provider, user.Id, "https://budget.example.test", output, timeout.Token)).ShouldBe(0);

        var link = new Uri(output.ToString().Trim());
        var token = TokenDecodeOutcome.Decode(QueryHelpers.ParseQuery(link.Query)["code"]).Value.ShouldBeOfType<TokenDecodeOutcome.DecodedToken>().Value;
        await database.Entry(user).ReloadAsync(timeout.Token);
        user.SecurityStamp.ShouldBe(stamp);
        user.EmailConfirmed.ShouldBeFalse();
        user.TwoFactorEnabled.ShouldBeTrue();
        (await users.ResetPasswordAsync(user, token, "Replacement-password!49")).Succeeded.ShouldBeTrue();
        await database.Entry(user).ReloadAsync(timeout.Token);
        user.SecurityStamp.ShouldNotBe(stamp, StringComparer.Ordinal);
        user.EmailConfirmed.ShouldBeFalse();
        user.TwoFactorEnabled.ShouldBeTrue();
        (await users.CheckPasswordAsync(user, "Replacement-password!49")).ShouldBeTrue();
        (await users.CheckPasswordAsync(user, "Original-password!37")).ShouldBeFalse();
        (await users.ResetPasswordAsync(user, token, "Another-password!53")).Succeeded.ShouldBeFalse();
        (await users.CheckPasswordAsync(user, "Replacement-password!49")).ShouldBeTrue();
    }

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
