namespace YHAB.SharedKernel.Budgeting;

public sealed record MonthReport(DateOnly Month, decimal Income, decimal Expense, decimal Assets, decimal Debt)
{
    public decimal NetIncome => Income - Expense;
    public decimal NetWorth => Assets - Debt;
}

