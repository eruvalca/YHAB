using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PlanSharingTests
{
    [Fact]
    public async Task SharingGrantsEditingAndReadAccessUntilTheOwnerRemovesItAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("Shared plan"), timeout.Token);

        var shared = await database.Sharing.ShareAsync("owner-a", id, "  b@example.test ", timeout.Token);
        shared.IsOwner.ShouldBeTrue();
        shared.OwnerName.ShouldBe("a@example.test");
        shared.Collaborators.ShouldBe([new("owner-b", "b@example.test")]);

        var sharedPlans = await database.Store.ListAsync("owner-b", timeout.Token);
        var summary = sharedPlans.ShouldHaveSingleItem();
        summary.Id.ShouldBe(id);
        summary.IsOwner.ShouldBeFalse();
        summary.SharedBy.ShouldBe("a@example.test");

        var plan = (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldNotBeNull();
        plan.IsOwner.ShouldBeFalse();
        plan.OwnerName.ShouldBe("a@example.test");
        (await database.Queries.ViewAsync("owner-b", id, timeout.Token)).Catalog.Id.ShouldBe(id);
        (await database.Queries.WorkspaceAsync("owner-b", id, null, timeout.Token)).View.Catalog.Version.ShouldBe(plan.Version);

        (await database.Store.ExecuteAsync("owner-b", id, new UpdatePlan(plan.Version, "Renamed together", "Shared note"), timeout.Token))
            .IsT0.ShouldBeTrue();
        var renamed = (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        renamed.Name.ShouldBe("Renamed together");
        renamed.Notes.ShouldBe("Shared note");

        (await database.Sharing.ReadAsync("owner-b", id, timeout.Token)).IsOwner.ShouldBeFalse();
        var forbidden = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.ShareAsync("owner-b", id, "a@example.test", timeout.Token));
        forbidden.Status.ShouldBe(404);

        var remaining = await database.Sharing.UnshareAsync("owner-a", id, "owner-b", timeout.Token);
        remaining.Collaborators.ShouldBeEmpty();
        (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldBeNull();
        (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldBeEmpty();
        await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.ViewAsync("owner-b", id, timeout.Token));
    }

    [Fact]
    public async Task SharingRejectsUnknownAddressesAndTheOwnerAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("Solo plan"), timeout.Token);

        var missing = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.ShareAsync("owner-a", id, "nobody@example.test", timeout.Token));
        missing.Status.ShouldBe(404);
        var self = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.ShareAsync("owner-a", id, "A@EXAMPLE.TEST", timeout.Token));
        self.Status.ShouldBe(400);

        await database.Sharing.ShareAsync("owner-a", id, "b@example.test", timeout.Token);
        var duplicate = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.ShareAsync("owner-a", id, "b@example.test", timeout.Token));
        duplicate.Status.ShouldBe(409);
    }

    [Fact]
    public async Task ASharedMemberCanLeaveButAnOwnerCannotAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("Leaving plan"), timeout.Token);
        await database.Sharing.ShareAsync("owner-a", id, "b@example.test", timeout.Token);

        var owner = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.LeaveAsync("owner-a", id, timeout.Token));
        owner.Status.ShouldBe(400);
        await database.Sharing.LeaveAsync("owner-b", id, timeout.Token);

        (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldBeEmpty();
        (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldBeNull();
        await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.ReadAsync("owner-b", id, timeout.Token));
    }
}
