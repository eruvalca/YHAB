using Aspire.Hosting.ApplicationModel;
using YHAB.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.ExecutionContext.IsRunMode
    ? builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithRepl()
    : null;
IResourceBuilder<IResourceWithConnectionString> database = postgres is not null
    ? postgres.AddDatabase("yhabdb", "yhab")
    : AzureDeployment.AddDatabase(builder);

var web = builder.AddProject<Projects.YHAB>("yhab")
    .WithReference(database)
    .WaitFor(database)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

// Pin the managed EF tool to the application's EF Core version rather than the global tool.
#pragma warning disable ASPIREDOTNETTOOL // The agreed Aspire EF migration integration uses the experimental tool resource API.
var migrations = web.AddEFMigrations("yhab-migrations", "YHAB.Data.ApplicationDbContext",
        tool => tool.WithToolVersion("10.0.12"))
    .WithReference(database)
    .WaitFor(database)
    // EF constructs the web host at design time but does not serve HTTP. Avoid inherited DCP endpoint tokens.
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:0")
    .WithMigrationOutputDirectory("Data/Migrations")
    .WithMigrationNamespace("YHAB.Migrations");
#pragma warning restore ASPIREDOTNETTOOL

if (postgres is not null)
{
    migrations.RunDatabaseUpdateOnStart();
    web.WaitForCompletion(migrations);
    postgres.WithPgAdmin(pgAdmin => pgAdmin.WithExplicitStart());
}
else
{
    AzureDeployment.Configure(builder, web, migrations);
}

// The host owns Ctrl+C/SIGTERM shutdown; there is no outer operation to cancel it.
await builder.Build().RunAsync(CancellationToken.None);
