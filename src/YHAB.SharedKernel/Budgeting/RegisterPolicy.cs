namespace YHAB.SharedKernel.Budgeting;

public static class RegisterPolicy
{
    public static void Validate(RegisterQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.PageSize is < 1 or > 100 || query.Search is null || query.Search.Length > 200 || query.From > query.Through
            || query.Sort is not ("Newest first" or "Oldest first" or "Payee" or "Amount")
            || query.Filter is not ("All posted" or "Needs approval" or "Uncleared" or "Cleared" or "Reconciled" or "Recurring"))
        {
            throw new BudgetRequestException(400, "Choose valid register filters and a page size of 1–100.");
        }
    }
}
