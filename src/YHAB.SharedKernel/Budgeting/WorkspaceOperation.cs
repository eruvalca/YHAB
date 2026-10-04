namespace YHAB.SharedKernel.Budgeting;



/// <summary>Pure save/refresh state. A successful write and a successful display refresh are separate facts.</summary>
public sealed record WorkspaceOperation(WorkspacePhase Phase = WorkspacePhase.Ready, PlanCommand? Pending = null, string Message = "All changes saved")
{
    public bool IsBusy => Phase is WorkspacePhase.Saving or WorkspacePhase.Refreshing;
    public bool CanEdit => Phase == WorkspacePhase.Ready;
    public bool CanRetry => Phase == WorkspacePhase.Uncertain && Pending is not null;
    public static WorkspaceOperation Start(PlanCommand command) => new(WorkspacePhase.Saving, command, "Saving…");
    public WorkspaceOperation Saved() => this with { Phase = WorkspacePhase.Refreshing, Message = "Saved. Updating balances…" };
    public WorkspaceOperation Loaded() => new(Message: Pending switch { UndoChange => "Change undone", RedoChange => "Change restored", _ => "All changes saved" });
    public static WorkspaceOperation Rejected() => new();
    public WorkspaceOperation Interrupted() => this with
    {
        Phase = Phase == WorkspacePhase.Refreshing ? WorkspacePhase.SavedNeedsRefresh : WorkspacePhase.Uncertain,
        Message = Phase == WorkspacePhase.Refreshing ? "Your change was saved. Refresh to update the displayed balances." : "Save status unknown. Retry safely or refresh to check your plan.",
    };
}
