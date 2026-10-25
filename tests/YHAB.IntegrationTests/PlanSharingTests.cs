using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PlanSharingTests
{
    [Fact]
    public async Task ShareCodeGrantsEditingAndReadAccessUntilTheOwnerRemovesItAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var time = new MutableTime();
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token, time);
        var id = await database.Store.CreateAsync("owner-a", new("Shared plan"), timeout.Token);

        var code = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        code.PlanId.ShouldBe(id);
        code.Code.ShouldNotBeNullOrWhiteSpace();
        code.ExpiresOn.ShouldBe(time.GetUtcNow().AddDays(7));

        (await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token)).ShouldBe(id);
        var shared = await database.Sharing.ReadAsync("owner-a", id, timeout.Token);
        shared.IsOwner.ShouldBeTrue();
        shared.OwnerName.ShouldBe("Alice");
        shared.Collaborators.ShouldBe([new("owner-b", "Bob")]);

        var sharedPlans = await database.Store.ListAsync("owner-b", timeout.Token);
        var summary = sharedPlans.ShouldHaveSingleItem();
        summary.Id.ShouldBe(id);
        summary.IsOwner.ShouldBeFalse();
        summary.SharedBy.ShouldBe("Alice");

        var plan = (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldNotBeNull();
        plan.IsOwner.ShouldBeFalse();
        plan.OwnerName.ShouldBe("Alice");
        shared.Collaborators.ShouldAllBe(collaborator => !collaborator.Name.Contains('@', StringComparison.Ordinal));
        (await database.Queries.ViewAsync("owner-b", id, timeout.Token)).Catalog.Id.ShouldBe(id);
        (await database.Queries.WorkspaceAsync("owner-b", id, null, timeout.Token)).View.Catalog.Version.ShouldBe(plan.Version);

        (await database.Store.ExecuteAsync("owner-b", id, new UpdatePlan(plan.Version, "Renamed together", "Shared note"), timeout.Token))
            .IsT0.ShouldBeTrue();
        var renamed = (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        renamed.Name.ShouldBe("Renamed together");
        renamed.Notes.ShouldBe("Shared note");

        (await database.Sharing.ReadAsync("owner-b", id, timeout.Token)).IsOwner.ShouldBeFalse();
        var forbidden = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.CreateShareCodeAsync("owner-b", id, timeout.Token));
        forbidden.Status.ShouldBe(404);

        var remaining = await database.Sharing.UnshareAsync("owner-a", id, "owner-b", timeout.Token);
        remaining.Collaborators.ShouldBeEmpty();
        (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldBeNull();
        (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldBeEmpty();
        await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.ViewAsync("owner-b", id, timeout.Token));
    }

    [Fact]
    public async Task JoinRejectsInvalidExpiredEmptyAndOwnerCodesAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var time = new MutableTime();
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token, time);
        var id = await database.Store.CreateAsync("owner-a", new("Solo plan"), timeout.Token);

        var invalid = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.JoinAsync("owner-b", "NOT-A-CODE", timeout.Token));
        invalid.Status.ShouldBe(404);
        var empty = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.JoinAsync("owner-b", "   ", timeout.Token));
        empty.Status.ShouldBe(400);

        var ownerCode = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        var self = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.JoinAsync("owner-a", ownerCode.Code, timeout.Token));
        self.Status.ShouldBe(400);

        var expiredCode = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        time.Advance(TimeSpan.FromDays(8));
        var expired = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.JoinAsync("owner-b", expiredCode.Code, timeout.Token));
        expired.Status.ShouldBe(404);
    }

    [Fact]
    public async Task ASharedMemberCanLeaveButAnOwnerCannotAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("Leaving plan"), timeout.Token);
        var code = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token);

        var owner = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.LeaveAsync("owner-a", id, timeout.Token));
        owner.Status.ShouldBe(400);
        await database.Sharing.LeaveAsync("owner-b", id, timeout.Token);

        (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldBeEmpty();
        (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldBeNull();
        await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.ReadAsync("owner-b", id, timeout.Token));
    }

    [Fact]
    public async Task CodesAreSingleUseAndRedemptionRetriesAreIdempotentAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("One-time plan"), timeout.Token);
        await AddUserAsync(database, "owner-c", "c@example.test", "Carol", timeout.Token);

        var code = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        (await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token)).ShouldBe(id);
        // A repeat of the same redemption, as after a lost acknowledgement, still succeeds.
        (await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token)).ShouldBe(id);
        var reused = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.JoinAsync("owner-c", code.Code, timeout.Token));
        reused.Status.ShouldBe(404);

        var secondCode = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        (await database.Sharing.JoinAsync("owner-b", secondCode.Code, timeout.Token)).ShouldBe(id);
        (await database.Sharing.ReadAsync("owner-a", id, timeout.Token)).Collaborators.ShouldBe([new("owner-b", "Bob")]);
    }

    [Fact]
    public async Task ConcurrentRedemptionIsIdempotentAndConcurrentRemovalHasOneWinnerAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("Contended plan"), timeout.Token);

        var code = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        var joins = await Task.WhenAll(
            database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token),
            database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token));
        joins.ShouldBe([id, id]);
        (await database.Sharing.ReadAsync("owner-a", id, timeout.Token)).Collaborators.ShouldBe([new("owner-b", "Bob")]);

        var removals = await Task.WhenAll(
            CaptureAsync(async () => { await database.Sharing.UnshareAsync("owner-a", id, "owner-b", timeout.Token); }),
            CaptureAsync(async () => { await database.Sharing.LeaveAsync("owner-b", id, timeout.Token); }));
        removals.Count(item => item is null).ShouldBe(1);
        removals.Single(item => item is not null)!.Status.ShouldBe(404);
        (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ConcurrentRedemptionAfterTheReceiptLookupDoesNotReportTheCodeAsInvalidAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var barrier = new JoinBarrier();
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token, interceptor: barrier);
        var id = await database.Store.CreateAsync("owner-a", new("Racing plan"), timeout.Token);
        var code = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        barrier.Arm();

        var first = database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token);
        var second = database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token);
        await barrier.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
        // Let one request commit while the other is paused after its receipt lookup.
        barrier.Release();
        await Task.WhenAny(first, second);
        barrier.Release();

        (await Task.WhenAll(first, second)).ShouldBe([id, id]);
        (await database.Sharing.ReadAsync("owner-a", id, timeout.Token)).Collaborators.ShouldBe([new("owner-b", "Bob")]);
    }

    [Fact]
    public async Task MissingDisplayNameFallsBackToAFriendlyNameInsteadOfTheEmailAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        await AddUserAsync(database, "owner-d", "d@example.test", null, timeout.Token);
        var id = await database.Store.CreateAsync("owner-d", new("Legacy plan"), timeout.Token);
        var code = await database.Sharing.CreateShareCodeAsync("owner-d", id, timeout.Token);
        (await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token)).ShouldBe(id);

        var sharing = await database.Sharing.ReadAsync("owner-b", id, timeout.Token);
        sharing.OwnerName.ShouldBe("Member");

        var plan = (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldNotBeNull();
        plan.OwnerName.ShouldBe("Member");
        var summary = (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldHaveSingleItem();
        summary.SharedBy.ShouldBe("Member");
    }

    [Fact]
    public async Task DisplayNameThatEqualsTheCurrentAccountEmailFallsBackToMemberAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        // A name saved before an email change can become the account's current address.
        await AddUserAsync(database, "owner-e", "new@example.test", "new@example.test", timeout.Token);
        await AddUserAsync(database, "owner-f", "f@example.test", "f@example.test", timeout.Token);
        var id = await database.Store.CreateAsync("owner-e", new("Renamed plan"), timeout.Token);
        var code = await database.Sharing.CreateShareCodeAsync("owner-e", id, timeout.Token);
        (await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token)).ShouldBe(id);
        var secondCode = await database.Sharing.CreateShareCodeAsync("owner-e", id, timeout.Token);
        (await database.Sharing.JoinAsync("owner-f", secondCode.Code, timeout.Token)).ShouldBe(id);

        var sharing = await database.Sharing.ReadAsync("owner-b", id, timeout.Token);
        sharing.OwnerName.ShouldBe("Member");
        sharing.Collaborators.ShouldBe([new("owner-b", "Bob"), new("owner-f", "Member")]);

        var plan = (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldNotBeNull();
        plan.OwnerName.ShouldBe("Member");
        var summary = (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldHaveSingleItem();
        summary.SharedBy.ShouldBe("Member");
    }

    [Fact]
    public async Task OwnerDeletesPlanAndCollaboratorsLoseAccessAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("Doomed plan"), timeout.Token);
        var plan = (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        var account = new AccountData(Guid.NewGuid(), "Checking", AccountKind.Checking, 500, plan.Today, false, "");
        (await database.Store.ExecuteAsync("owner-a", id, new SaveAccount(plan.Version, account), timeout.Token)).IsT0.ShouldBeTrue();
        var code = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token);

        var wrongName = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.DeleteAsync("owner-a", id, "doomed plan", timeout.Token));
        wrongName.Status.ShouldBe(400);
        var missingName = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.DeleteAsync("owner-a", id, "   ", timeout.Token));
        missingName.Status.ShouldBe(400);
        var notOwner = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.DeleteAsync("owner-b", id, "Doomed plan", timeout.Token));
        notOwner.Status.ShouldBe(404);
        (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldNotBeNull();

        await database.Sharing.DeleteAsync("owner-a", id, " Doomed plan ", timeout.Token);

        (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldBeNull();
        (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldBeNull();
        (await database.Store.ListAsync("owner-a", timeout.Token)).ShouldBeEmpty();
        (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldBeEmpty();
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Queries.ViewAsync("owner-b", id, timeout.Token))).Status.ShouldBe(404);

        await using var verify = await database.Factory.CreateDbContextAsync(timeout.Token);
        (await verify.Set<BudgetPlan>().AnyAsync(item => item.Id == id, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetPlanMember>().AnyAsync(item => item.PlanId == id, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetPlanShareCode>().AnyAsync(item => item.PlanId == id, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetPlanShareCodeRedemption>().AnyAsync(item => item.PlanId == id, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetReceipt>().AnyAsync(item => item.PlanId == id, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetAccount>().AnyAsync(item => item.PlanId == id, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetCategory>().AnyAsync(item => item.PlanId == id, timeout.Token)).ShouldBeFalse();
        (await verify.Set<BudgetHistory>().AnyAsync(item => item.PlanId == id, timeout.Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task TransferOwnershipMakesTheCollaboratorOwnerAndKeepsThePreviousOwnerAsAMemberAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token);
        await AddUserAsync(database, "owner-c", "c@example.test", "Cara", timeout.Token);
        var id = await database.Store.CreateAsync("owner-a", new("Handover plan"), timeout.Token);
        var code = await database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token);
        await database.Sharing.JoinAsync("owner-b", code.Code, timeout.Token);
        var version = (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull().Version;

        var notOwner = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.TransferOwnershipAsync("owner-b", id, "owner-a", timeout.Token));
        notOwner.Status.ShouldBe(404);
        var notMember = await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.TransferOwnershipAsync("owner-a", id, "owner-c", timeout.Token));
        notMember.Status.ShouldBe(404);

        var transferred = await database.Sharing.TransferOwnershipAsync("owner-a", id, "owner-b", timeout.Token);
        transferred.IsOwner.ShouldBeFalse();
        transferred.OwnerName.ShouldBe("Bob");
        transferred.Collaborators.ShouldBe([new("owner-a", "Alice")]);
        (await database.Sharing.ReadAsync("owner-b", id, timeout.Token)).IsOwner.ShouldBeTrue();

        var previous = (await database.Store.ListAsync("owner-a", timeout.Token)).ShouldHaveSingleItem();
        previous.IsOwner.ShouldBeFalse();
        previous.SharedBy.ShouldBe("Bob");
        (await database.Store.ListAsync("owner-b", timeout.Token)).ShouldHaveSingleItem().IsOwner.ShouldBeTrue();
        (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull().IsOwner.ShouldBeFalse();
        // Handing over access is not a ledger change, so the plan revision is untouched.
        (await database.Store.ReadAsync("owner-b", id, timeout.Token)).ShouldNotBeNull().Version.ShouldBe(version);
        (await Should.ThrowAsync<BudgetRequestException>(() => database.Sharing.CreateShareCodeAsync("owner-a", id, timeout.Token)))
            .Status.ShouldBe(404);

        // The outgoing owner keeps editing access and may leave like any collaborator.
        (await database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(version, "Handed over", ""), timeout.Token)).IsT0.ShouldBeTrue();
        await database.Sharing.LeaveAsync("owner-a", id, timeout.Token);

        (await database.Store.ListAsync("owner-a", timeout.Token)).ShouldBeEmpty();
        (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldBeNull();
        (await database.Sharing.ReadAsync("owner-b", id, timeout.Token)).Collaborators.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletionRejectsAPlanRenamedAfterTheConfirmationReadAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var barrier = new DeleteBarrier();
        await using var database = await BudgetDatabase.CreateAsync(timeout.Token, interceptor: barrier);
        var id = await database.Store.CreateAsync("owner-a", new("Doomed plan"), timeout.Token);
        barrier.Arm();

        var deletion = database.Sharing.DeleteAsync("owner-a", id, "Doomed plan", timeout.Token);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
        var plan = (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull();
        (await database.Store.ExecuteAsync("owner-a", id, new UpdatePlan(plan.Version, "Renamed plan", ""), timeout.Token)).IsT0.ShouldBeTrue();
        barrier.Release();

        var conflict = await Should.ThrowAsync<BudgetRequestException>(() => deletion);
        conflict.Status.ShouldBe(409);
        (await database.Store.ReadAsync("owner-a", id, timeout.Token)).ShouldNotBeNull().Name.ShouldBe("Renamed plan");
    }

    private static async Task<BudgetRequestException?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (BudgetRequestException exception)
        {
            return exception;
        }
    }

    private static async Task AddUserAsync(BudgetDatabase database, string id, string email, string? displayName, CancellationToken cancellationToken)
    {
        await using var context = await database.Factory.CreateDbContextAsync(cancellationToken);
        context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed class MutableTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    // Pauses the conditional delete so the test can change the plan after ownership and the
    // confirmed name were read but before the delete predicate runs.
    private sealed class DeleteBarrier : DbCommandInterceptor, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public void Release() => _gate.Release();
        public void Dispose() => _gate.Dispose();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"BudgetPlans\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.TrySetResult();
                await _gate.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    // Holds both invitation lookups until the test releases them, forcing one join to
    // commit after the other has already missed the code in its receipt lookup.
    private sealed class JoinBarrier : DbCommandInterceptor, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _arrivals;
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Armed { get; set; }
        public void Arm() => Armed = true;
        public void Release() => _gate.Release();
        public void Dispose() => _gate.Dispose();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("\"BudgetPlanShareCodes\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                {
                    BothEntered.TrySetResult();
                }
                await _gate.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}

