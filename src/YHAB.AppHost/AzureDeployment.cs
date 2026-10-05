using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.EntityFrameworkCore;
using Aspire.Hosting.Pipelines;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.PostgreSql;

namespace YHAB.AppHost;

internal static class AzureDeployment
{
    internal static IResourceBuilder<IResourceWithConnectionString> AddDatabase(IDistributedApplicationBuilder builder)
    {
        var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
            .ConfigureInfrastructure(infrastructure =>
            {
                var server = infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();
                // Keep the initial deployment small. These are explicit, reviewable production defaults.
                server.Sku.Name = "Standard_B1ms";
                server.Sku.Tier = PostgreSqlFlexibleServerSkuTier.Burstable;
                server.StorageSizeInGB = 32;
                server.Backup.BackupRetentionDays = 7;
                // PostgreSQL 18 is supported by Azure; the provisioning SDK's enum currently ends at 16.
                server.Version = new StringLiteralExpression("18");
            });
        return postgres.AddDatabase("yhabdb", "yhab");
    }

    internal static void Configure(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> web,
        IResourceBuilder<EFMigrationResource> migrations)
    {
        var logs = builder.AddAzureLogAnalyticsWorkspace("logs");
        var insights = builder.AddAzureApplicationInsights("insights", logs);
        builder.AddAzureContainerAppEnvironment("yhab-environment")
            .WithAzureLogAnalyticsWorkspace(logs);
        // PostgreSQL object ownership must be shared by the migration bundle and the web application.
        var databaseIdentity = builder.AddAzureUserAssignedIdentity("yhab-database-identity");

        web.WithAzureUserAssignedIdentity(databaseIdentity)
            .WithReference(insights)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
            .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
            .WithEnvironment("HealthChecks__ExposeEndpoints", "true")
            .PublishAsAzureContainerApp((_, app) =>
            {
                // The recurring worker needs an active process; start with one Blazor server replica.
                app.Template.Scale.MinReplicas = 1;
                app.Template.Scale.MaxReplicas = 1;
                var container = app.Template.Containers[0].Unwrap();
                container.Probes.Add(new ContainerAppProbe
                {
                    ProbeType = ContainerAppProbeType.Liveness,
                    HttpGet = CreateProbeRequest("/alive", app),
                    InitialDelaySeconds = 10,
                    PeriodSeconds = 10,
                    TimeoutSeconds = 2,
                    FailureThreshold = 3,
                });
                container.Probes.Add(new ContainerAppProbe
                {
                    ProbeType = ContainerAppProbeType.Readiness,
                    HttpGet = CreateProbeRequest("/health", app),
                    PeriodSeconds = 10,
                    TimeoutSeconds = 7,
                    FailureThreshold = 3,
                });
            });

        migrations.WithAzureUserAssignedIdentity(databaseIdentity)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
            .WithEnvironment("DOTNET_ENVIRONMENT", "Production")
            .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
            .PublishAsAzureContainerAppJob((_, job) =>
            {
                job.Name = migrations.Resource.Name;
                job.Configuration.TriggerType = ContainerAppJobTriggerType.Manual;
                job.Configuration.ReplicaTimeout = 600;
                job.Configuration.ReplicaRetryLimit = 0;
                job.Configuration.ManualTriggerConfig.Parallelism = 1;
                job.Configuration.ManualTriggerConfig.ReplicaCompletionCount = 1;
                // EF's --connection override discards the enriched data source, including Azure authentication and TLS.
                // Read ConnectionStrings__yhabdb through the real application startup instead of the image's default command.
                var container = job.Template.Containers[0].Unwrap();
                container.Command.Add("/app/efbundle");
                container.Args.Add("--verbose");
            });

        ConfigureMigrationGate(builder, web.Resource, migrations.Resource);
    }

    private static ContainerAppHttpRequestInfo CreateProbeRequest(string path, ContainerApp app) => new()
    {
        Path = path,
        Port = app.Configuration.Ingress.TargetPort,
        // Internal probes use HTTP behind the TLS-terminating ingress. Exercise the endpoint, not an HTTPS redirect.
        HttpHeaders = { new ContainerAppHttpHeaderInfo { Name = "X-Forwarded-Proto", Value = "https" } },
    };

#pragma warning disable ASPIREPIPELINES001 // Aspire's deployment pipeline API is needed to gate web provisioning on a successful migration.
    private static void ConfigureMigrationGate(IDistributedApplicationBuilder builder, ProjectResource web, EFMigrationResource migrations)
    {
        const string MigrationStep = "apply-yhab-migrations";
        builder.Pipeline.AddStep(MigrationStep,
            context => DeploymentMigrations.RunAsync(builder.Configuration, migrations.Name, context.CancellationToken));
        builder.Pipeline.AddPipelineConfiguration(context =>
        {
            var migrationTarget = migrations.GetDeploymentTargetAnnotation()?.DeploymentTarget;
            var webTarget = web.GetDeploymentTargetAnnotation()?.DeploymentTarget;
            // Aspire resolves the graph once before it creates Azure deployment targets, then again for deployment.
            if (migrationTarget is null && webTarget is null)
            {
                return Task.CompletedTask;
            }

            migrationTarget = migrationTarget
                ?? throw new InvalidOperationException("The migration job has no Azure deployment target.");
            webTarget = webTarget
                ?? throw new InvalidOperationException("The web application has no Azure deployment target.");
            var gate = context.Steps.Single(step => string.Equals(step.Name, MigrationStep, StringComparison.Ordinal));
            var migrationSteps = context.GetSteps(migrationTarget, WellKnownPipelineTags.ProvisionInfrastructure).ToArray();
            var webSteps = context.GetSteps(webTarget, WellKnownPipelineTags.ProvisionInfrastructure).ToArray();
            if (migrationSteps.Length == 0 || webSteps.Length == 0)
            {
                throw new InvalidOperationException("Azure provisioning steps are missing; refusing an ungated web deployment.");
            }

            migrationSteps.RequiredBy(gate);
            webSteps.DependsOn(gate);
            return Task.CompletedTask;
        });
    }
#pragma warning restore ASPIREPIPELINES001
}
