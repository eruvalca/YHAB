using Microsoft.EntityFrameworkCore;
using Shouldly;
using YHAB.Features.Budgeting.Data;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.IntegrationTests;

internal sealed record LedgerScaleFixture(Guid Id, AccountData[] Accounts, CategoryData[] Categories, DateOnly Start, DateOnly Month, int Entries, int Months)
{
    public decimal Cash => Accounts.Length * 1_000_000m + Entries / 10 * 91m;

    public static async Task<LedgerScaleFixture> CreateAsync(BudgetDatabase database, int entries, int accounts, int categories, int months, CancellationToken token)
    {
        var id = await database.Store.CreateAsync("owner-a", new("Scale fixture", StarterCategories: false), token);
        var empty = (await database.Store.ReadAsync("owner-a", id, token)).ShouldNotBeNull();
        var month = BudgetFacts.Month(empty.Today);
        var start = month.AddMonths(1 - months);
        var group = new GroupData(Guid.CreateVersion7(), "Everyday", 0);
        var accountData = Enumerable.Range(0, accounts).Select(index => new AccountData(Guid.CreateVersion7(), $"Account {index:D3}", AccountKind.Checking, 1_000_000, start, false, "")).ToArray();
        var categoryData = Enumerable.Range(0, categories).Select(index => new CategoryData(Guid.CreateVersion7(), group.Id, $"Category {index:D3}", "", index, false, null,
            new(TargetKind.Balance, TargetCadence.Monthly, 2000, start, null))).ToArray();
        var snapshot = empty with { Accounts = accountData, Groups = [group], Categories = categoryData };
        await using var context = await database.Factory.CreateDbContextAsync(token);
        BudgetSnapshotMapping.Apply(context, snapshot);
        await context.SaveChangesAsync(token);
        // Direct synthetic setup only. The operation and query measurements use production services.
        var days = empty.Today.DayNumber - start.DayNumber + 1;
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "BudgetAllocation" ("PlanId", "CategoryId", "Month", "Amount", "Snoozed")
            SELECT {id}, "Id", ({start} + make_interval(months => n))::date, 1, false
            FROM "BudgetCategory" CROSS JOIN generate_series(0, {months - 1}) n WHERE "PlanId" = {id};
            INSERT INTO "BudgetTransaction" ("PlanId", "Id", "AccountId", "Date", "Payee", "Memo", "Amount", "State", "TransferState", "NeedsApproval", "Flag", "Repeat", "Occurrence", "Sequence")
            SELECT {id}, gen_random_uuid(), ({accountData.Select(item => item.Id).ToArray()})[1 + (n % {accounts})::int],
                {start} + (n % {days})::int, CASE WHEN n % 10 = 0 THEN 'Salary' ELSE 'Market' END,
                '', CASE WHEN n % 10 = 0 THEN 100 ELSE -1 END, 0, 0, false, '', 0, 0, n
            FROM generate_series(1, {entries}) n;
            INSERT INTO "BudgetSplit" ("PlanId", "Id", "TransactionId", "CategoryId", "Amount", "Memo")
            SELECT "PlanId", gen_random_uuid(), "Id", CASE WHEN "Amount" < 0 THEN ({categoryData.Select(item => item.Id).ToArray()})[1 + ("Sequence" % {categories})::int] ELSE NULL END, "Amount", ''
            FROM "BudgetTransaction" WHERE "PlanId" = {id};
            UPDATE "BudgetPlans" SET "NextSequence" = {entries} WHERE "Id" = {id};
            """, token);
        return new(id, accountData, categoryData, start, month, entries, months);
    }
}
