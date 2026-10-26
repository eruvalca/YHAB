using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class WorkspaceOperationTests
{
    [Fact]
    public void InterruptedWriteRetainsTheSameOperationForSafeRetry()
    {
        var command = new UndoChange(3);
        var saving = WorkspaceOperation.Start(command);
        saving.IsBusy.ShouldBeTrue();
        saving.CanEdit.ShouldBeFalse();
        saving.CanRetry.ShouldBeFalse();
        var uncertain = saving.Interrupted();
        uncertain.Phase.ShouldBe(WorkspacePhase.Uncertain);
        uncertain.CanRetry.ShouldBeTrue();
        uncertain.CanEdit.ShouldBeFalse();
        uncertain.IsBusy.ShouldBeFalse();
        uncertain.Pending.ShouldBeSameAs(command);
        uncertain.Message.ShouldContain("unknown");
        WorkspaceOperation.Start(uncertain.Pending!).Pending!.OperationId.ShouldBe(command.OperationId);
        (uncertain with { Pending = null }).CanRetry.ShouldBeFalse();
    }

    [Fact]
    public void RefreshFailureAfterCommitNeverOffersToSaveAgain()
    {
        var saved = WorkspaceOperation.Start(new RedoChange(3)).Saved();
        saved.IsBusy.ShouldBeTrue();
        saved.Message.ShouldContain("Saved");
        var interrupted = saved.Interrupted();
        interrupted.Phase.ShouldBe(WorkspacePhase.SavedNeedsRefresh);
        interrupted.CanRetry.ShouldBeFalse();
        interrupted.CanEdit.ShouldBeFalse();
        interrupted.Message.ShouldContain("was saved");
        var loaded = interrupted.Loaded();
        loaded.Message.ShouldBe("Change restored");
        loaded.CanEdit.ShouldBeTrue();
        loaded.Pending.ShouldBeNull();
        loaded.CanRetry.ShouldBeFalse();
        loaded.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public void SuccessfulUndoAndRejectedWritesHaveDistinctCompletionMessages()
    {
        WorkspaceOperation.Start(new UndoChange(3)).Saved().Loaded().Message.ShouldBe("Change undone");
        WorkspaceOperation.Start(new UpdatePlan(3, "Plan", "")).Saved().Loaded().Message.ShouldBe("All changes saved");
        var rejected = WorkspaceOperation.Rejected();
        rejected.Message.ShouldBe("Change not saved.");
        rejected.CanEdit.ShouldBeTrue();
        rejected.CanRetry.ShouldBeFalse();
    }

    [Fact]
    public void RefreshPostedCommandKeepsItsDraftIntentThroughFailureAndRetry()
    {
        var command = new PostRecurring(7, new DateOnly(2026, 10, 3));
        var saving = WorkspaceOperation.Start(command, preservesDraft: true);
        saving.PreservesDraft.ShouldBeTrue();

        // The write committed but its follow-up read failed: the draft it never consumed stays
        // preserved, so the next refresh must not treat it as saved.
        var interrupted = saving.Saved().Interrupted();
        interrupted.Phase.ShouldBe(WorkspacePhase.SavedNeedsRefresh);
        interrupted.PreservesDraft.ShouldBeTrue();
        interrupted.CanRetry.ShouldBeFalse();
        saving.Interrupted().PreservesDraft.ShouldBeTrue();

        // A retry repeats the same command with the same intent, and a settled load clears it.
        WorkspaceOperation.Start(interrupted.Pending!, interrupted.PreservesDraft).PreservesDraft.ShouldBeTrue();
        interrupted.Loaded().PreservesDraft.ShouldBeFalse();

        // The editor's own save consumes its draft, so it never preserves one.
        WorkspaceOperation.Start(command).PreservesDraft.ShouldBeFalse();
        WorkspaceOperation.Start(command).Saved().Interrupted().PreservesDraft.ShouldBeFalse();
    }
}
