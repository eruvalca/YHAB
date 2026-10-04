using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using YHAB.Tests.Scenarios;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class HouseholdProgressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 200)]
    [InlineData(2, 200)]
    [InlineData(3, 200)]
    [InlineData(1, 1000)]
    [InlineData(2, 1000)]
    [InlineData(3, 1000)]
    public void MonthlyLedgerMatchesIndependentTotals(int months, int monthlyCount)
    {
        var plan = HouseholdScenario.Create(months, monthlyCount);
        var timer = Stopwatch.StartNew();
        HouseholdChecks.Verify(plan, months, monthlyCount);
        // Input ordering must not affect chronology, reserves, or report totals.
        HouseholdChecks.Verify(plan with { Transactions = plan.Transactions.Reverse().ToArray() }, months, monthlyCount);
        output.WriteLine($"{months} months / {plan.Transactions.Count} entries: totals and reverse-order checks {timer.ElapsedMilliseconds} ms.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReconciliationLocksOnlyTheSelectedSideAndRejectsUnapprovedAdjustments(int months)
    {
        var plan = HouseholdScenario.Create(months, 1000);
        var checking = HouseholdScenario.Id(1);
        var balance = 5400 + 3880 * months;
        var rejected = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new ReconcileAccount(0, checking, plan.Today, balance + 1, false), plan.Today);
        rejected.IsT1.ShouldBeTrue();
        var reconciled = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new ReconcileAccount(0, checking, plan.Today, balance, false), plan.Today);
        reconciled.IsT0.ShouldBeTrue();
        var updated = reconciled.AsT0;
        updated.Transactions.Where(item => item.AccountId == checking).ShouldAllBe(item => item.State == ClearingState.Reconciled);
        updated.Transactions.Where(item => item.AccountId != checking).ShouldAllBe(item => item.State == ClearingState.Cleared);
        updated.Transactions.Where(item => item.TransferAccountId.HasValue).ShouldAllBe(item => item.TransferState == ClearingState.Cleared);
        var purchase = updated.Transactions.First(item => item.AccountId == checking && item.Payee.StartsWith("Market", StringComparison.Ordinal));
        PlanCommandHandler.Apply(BudgetTestData.NewIds(), updated, new SaveTransaction(0, purchase with { Memo = "Changed" }), plan.Today).IsT1.ShouldBeTrue();
        PlanCommandHandler.Apply(BudgetTestData.NewIds(), updated, new DeleteTransactions(0, [purchase.Id]), plan.Today).IsT1.ShouldBeTrue();
        HouseholdChecks.Verify(updated, months, 1000);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RecurrenceCrossesShortMonthsWithoutDuplicateOccurrences(int months)
    {
        var plan = HouseholdScenario.Create(months, 1000);
        var template = new TransactionData(Guid.NewGuid(), HouseholdScenario.Id(1), new(2026, 1, 31), "Monthly service", "", -12,
            null, ClearingState.Uncleared, ClearingState.Uncleared, false, "", [new(Guid.NewGuid(), HouseholdScenario.Id(106), -12, "")], RepeatFrequency.Monthly);
        var result = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new SaveTransaction(0, template), plan.Today);
        result.IsT0.ShouldBeTrue();
        var posted = result.AsT0;
        var occurrences = posted.Transactions.Where(item => item.SourceTemplateId == template.Id).OrderBy(item => item.Date).ToArray();
        occurrences.Select(item => item.Date).ShouldBe(Enumerable.Range(1, months).Select(HouseholdScenario.End));
        occurrences.ShouldAllBe(item => item.NeedsApproval && item.ScheduledDate == item.Date);
        var pending = posted.Transactions.Single(item => item.Id == template.Id);
        pending.Date.ShouldBe(HouseholdScenario.End(months + 1));
        var again = PlanCommandHandler.Apply(BudgetTestData.NewIds(), posted, new PostRecurring(0, plan.Today), plan.Today);
        again.IsT0.ShouldBeTrue();
        again.AsT0.Transactions.Count.ShouldBe(plan.Transactions.Count + months + 1);
        BudgetFacts.Balance(again.AsT0, plan.Accounts[0], plan.Today).Working.ShouldBe(5400 + (3880 - 12) * months);
        ReportCalculator.Months(again.AsT0, HouseholdScenario.Start, plan.Today).ShouldAllBe(item => item.Expense == 3932);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SplitValidationAndReportDateBoundariesRemainExact(int months)
    {
        var plan = HouseholdScenario.Create(months, 1000);
        var entry = plan.Transactions.First(item => item.Splits.Count == 2);
        var malformed = entry with { Splits = [entry.Splits[0] with { Amount = entry.Splits[0].Amount + .01m }, entry.Splits[1]] };
        PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new SaveTransaction(0, malformed), plan.Today).IsT1.ShouldBeTrue();
        PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new SaveTransaction(0, entry with { Date = plan.Today.AddDays(1) }), plan.Today).IsT1.ShouldBeTrue();
        var first = HouseholdScenario.Start.AddMonths(months - 1);
        var salariesOnly = ReportCalculator.Months(plan, first.AddDays(14), first.AddDays(14)).Single();
        salariesOnly.Income.ShouldBe(4200);
        var refundOnly = ReportCalculator.Spending(plan, first.AddDays(25), first.AddDays(25));
        refundOnly.Single().Amount.ShouldBe(-50);
        var transferDay = ReportCalculator.Months(plan, first.AddDays(27), first.AddDays(27)).Single();
        transferDay.Income.ShouldBe(0);
        transferDay.Expense.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CashAndCreditOverspendingRollOverDifferentlyAndCanBeCovered(int months)
    {
        var plan = HouseholdScenario.Create(months, 1000);
        // Housing has only cash spending; annual bills has no earlier purchases.
        // Mixed cash/card categories would legitimately redistribute existing reserves.
        var cash = new TransactionData(HouseholdScenario.Id(900001), HouseholdScenario.Id(1), plan.Today, "Unexpected cash bill", "",
            -100, null, ClearingState.Cleared, ClearingState.Uncleared, false, "",
            [new(Guid.NewGuid(), HouseholdScenario.Id(100), -100, "")]);
        var credit = new TransactionData(HouseholdScenario.Id(900002), HouseholdScenario.Id(3), plan.Today, "Unexpected card bill", "",
            -(150 * months + 70), null, ClearingState.Cleared, ClearingState.Uncleared, false, "",
            [new(Guid.NewGuid(), HouseholdScenario.Id(105), -(150 * months + 70), "")]);
        var first = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new SaveTransaction(0, cash), plan.Today);
        first.IsT0.ShouldBeTrue();
        var second = PlanCommandHandler.Apply(BudgetTestData.NewIds(), first.AsT0, new SaveTransaction(0, credit), plan.Today);
        second.IsT0.ShouldBeTrue();
        var overspent = second.AsT0;
        var month = BudgetFacts.Month(plan.Today);
        var budget = BudgetCalculator.Calculate(overspent, month, plan.Today);
        budget.CashOverspending.ShouldBe(100);
        budget.CreditOverspending.ShouldBe(70);
        budget.ReadyToAssign.ShouldBe(13400 + 4050 * months);
        var next = BudgetCalculator.Calculate(overspent, month.AddMonths(1), month.AddMonths(1));
        next.CashOverspending.ShouldBe(0);
        next.CreditOverspending.ShouldBe(0);
        next.ReadyToAssign.ShouldBe(13300 + 4050 * months);
        next.Categories.Single(item => item.Category.Id == HouseholdScenario.Id(108)).Available.ShouldBe(150 * months);
        var covered = PlanCommandHandler.Apply(BudgetTestData.NewIds(), overspent, new AutoAssign(0, month), plan.Today);
        covered.IsT0.ShouldBeTrue();
        var funded = BudgetCalculator.Calculate(covered.AsT0, month, plan.Today);
        funded.CashOverspending.ShouldBe(0);
        funded.CreditOverspending.ShouldBe(0);
        funded.ReadyToAssign.ShouldBe(13230 + 4050 * months);
        funded.Categories.Single(item => item.Category.Id == HouseholdScenario.Id(108)).Available.ShouldBe(150 * months + 70);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TargetFundingHonorsCarryoverHiddenAndSnoozedCategories(int months)
    {
        var plan = HouseholdScenario.Create(months, 1000);
        var month = BudgetFacts.Month(plan.Today);
        var refill = plan.Categories[2] with { Target = new(TargetKind.Refill, TargetCadence.Monthly, 1000, HouseholdScenario.Start, null) };
        var hidden = plan.Categories[3] with { Hidden = true, Target = new(TargetKind.SetAside, TargetCadence.Monthly, 1000, HouseholdScenario.Start, null) };
        var snoozed = plan.Categories[4] with { Target = new(TargetKind.SetAside, TargetCadence.Monthly, 1000, HouseholdScenario.Start, null) };
        var replacements = new Dictionary<Guid, CategoryData> { [refill.Id] = refill, [hidden.Id] = hidden, [snoozed.Id] = snoozed };
        plan = plan with
        {
            Categories = plan.Categories.Select(item => replacements.GetValueOrDefault(item.Id, item)).ToArray(),
            Allocations = plan.Allocations.Select(item => item.Month == month && item.CategoryId == snoozed.Id ? item with { Snoozed = true } : item).ToArray(),
        };
        var need = 150 - 40 * (months - 1);
        var before = BudgetCalculator.Calculate(plan, month, plan.Today);
        before.Categories.Single(item => item.Category.Id == refill.Id).TargetNeeded.ShouldBe(need);
        var result = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new AutoAssign(0, month), plan.Today);
        result.IsT0.ShouldBeTrue();
        var after = BudgetCalculator.Calculate(result.AsT0, month, plan.Today);
        after.Categories.Single(item => item.Category.Id == refill.Id).Available.ShouldBe(190);
        after.Categories.Single(item => item.Category.Id == hidden.Id).Assigned.ShouldBe(400);
        after.Categories.Single(item => item.Category.Id == snoozed.Id).Assigned.ShouldBe(350);
        after.ReadyToAssign.ShouldBe(13400 + 4050 * months - need);
        PlanCommandHandler.Apply(BudgetTestData.NewIds(), result.AsT0, new MoveMoney(0, refill.Id, hidden.Id, month, 190.01m), plan.Today).IsT1.ShouldBeTrue();
    }
}
