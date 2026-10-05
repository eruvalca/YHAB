using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.AppHost;

namespace YHAB.AspireIntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class DeploymentMigrationsTests
{
    [Fact]
    public async Task SuccessfulExecutionAllowsDeploymentAsync()
    {
        var job = CreateJob(JobExecutionRunningState.Succeeded);

        await DeploymentMigrations.RunAsync(job, TestContext.Current.CancellationToken);

        await job.Received(1).GetContainerAppJobExecutionAsync("execution-under-test", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Stopped")]
    public async Task UnsuccessfulExecutionBlocksDeploymentAsync(string status)
    {
        var job = CreateJob(new JobExecutionRunningState(status));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => DeploymentMigrations.RunAsync(job, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("execution-under-test");
        exception.Message.ShouldContain(status);
    }

    [Fact]
    public async Task CancellationStopsWaitingForRunningExecutionAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var job = CreateJob(JobExecutionRunningState.Running);
        job.When(resource => resource.GetContainerAppJobExecutionAsync("execution-under-test", Arg.Any<CancellationToken>()))
            .Do(_ => cancellation.Cancel());

        await Should.ThrowAsync<OperationCanceledException>(() => DeploymentMigrations.RunAsync(job, cancellation.Token));
    }

    [Fact]
    public async Task AlreadyCanceledDeploymentDoesNotStartJobAsync()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var job = CreateJob(JobExecutionRunningState.Succeeded);

        await Should.ThrowAsync<OperationCanceledException>(() => DeploymentMigrations.RunAsync(job, cancellation.Token));

        await job.DidNotReceive().StartAsync(Arg.Any<WaitUntil>(), Arg.Any<ContainerAppJobExecutionTemplate>(), Arg.Any<CancellationToken>());
    }

    private static ContainerAppJobResource CreateJob(JobExecutionRunningState status)
    {
        var job = Substitute.For<ContainerAppJobResource>();
        var operation = Substitute.For<ArmOperation<ContainerAppJobExecutionBase>>();
        operation.Value.Returns(ArmAppContainersModelFactory.ContainerAppJobExecutionBase(name: "execution-under-test"));
        job.StartAsync(WaitUntil.Completed, cancellationToken: Arg.Any<CancellationToken>()).Returns(operation);
        var execution = Substitute.For<ContainerAppJobExecutionResource>();
        execution.Data.Returns(ArmAppContainersModelFactory.ContainerAppJobExecutionData(
            id: null, name: "execution-under-test", resourceType: default, systemData: null, status: status,
            startOn: null, endOn: null, template: null, reason: null));
        job.GetContainerAppJobExecutionAsync("execution-under-test", Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(execution, Substitute.For<Response>()));
        return job;
    }
}
