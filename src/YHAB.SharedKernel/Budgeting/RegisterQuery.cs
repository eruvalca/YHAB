namespace YHAB.SharedKernel.Budgeting;

public sealed record RegisterQuery(Guid? AccountId = null, string Search = "", string Filter = "All posted", string Sort = "Newest first",
    DateOnly? From = null, DateOnly? Through = null, RegisterCursor? After = null, int PageSize = 50, long? Version = null);
