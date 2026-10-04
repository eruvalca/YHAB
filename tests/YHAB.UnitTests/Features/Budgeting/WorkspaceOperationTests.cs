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
        WorkspaceOperation.Rejected().ShouldBe(new WorkspaceOperation());
    }
}
