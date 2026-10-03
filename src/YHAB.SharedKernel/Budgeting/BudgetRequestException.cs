namespace YHAB.SharedKernel.Budgeting;

/// <summary>A safe, user-facing validation, ownership, or stale-edit response.</summary>
public sealed class BudgetRequestException(int status, string? message, Exception? innerException = null) : Exception(message, innerException)
{
    public BudgetRequestException() : this(400, "The budget request could not be completed.") { }
    public BudgetRequestException(string message) : this(400, message) { }
    public BudgetRequestException(string message, Exception innerException) : this(400, message, innerException) { }

    public int Status { get; } = status;
}
