using Azure;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Microsoft.Extensions.Configuration;

namespace YHAB.AppHost;

internal static class DeploymentMigrations
{
    internal static Task RunAsync(IConfiguration configuration, string jobName, CancellationToken cancellationToken)
    {
        var subscription = configuration["Azure:SubscriptionId"]
            ?? throw new InvalidOperationException("Set Azure__SubscriptionId before deploying.");
        var resourceGroup = configuration["Azure:ResourceGroup"]
            ?? throw new InvalidOperationException("Set Azure__ResourceGroup before deploying.");
        // Both local deployment and GitHub's OIDC azure/login step authenticate the Azure CLI.
        var client = new ArmClient(new AzureCliCredential());
        var id = ContainerAppJobResource.CreateResourceIdentifier(subscription, resourceGroup, jobName);
        var job = client.GetContainerAppJobResource(id);
        return RunAsync(job, cancellationToken);
    }

    internal static async Task RunAsync(ContainerAppJobResource job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));

        var start = await job.StartAsync(WaitUntil.Completed, cancellationToken: timeout.Token);
        var executionName = start.Value.Name;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            var execution = await job.GetContainerAppJobExecutionAsync(executionName, timeout.Token);
            var status = execution.Value.Data.Status;
            if (status == JobExecutionRunningState.Succeeded)
            {
                return;
            }

            if (status == JobExecutionRunningState.Failed || status == JobExecutionRunningState.Stopped)
            {
                throw new InvalidOperationException($"Migration execution '{executionName}' ended with '{status}'. The web deployment was not started.");
            }
        }
        while (await timer.WaitForNextTickAsync(timeout.Token));
    }
}
