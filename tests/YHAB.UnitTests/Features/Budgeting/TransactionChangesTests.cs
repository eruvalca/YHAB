using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Models;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class TransactionChangesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectivePostingCombinesStableIdentitiesWithLoadedOccurrences(bool save)
    {
        var plan = Create();
        var template = Entry(plan, 0, -25, date: January) with { Repeat = RepeatFrequency.Daily };
        var existing = template with
        {
            Id = Guid.NewGuid(),
            Repeat = RepeatFrequency.None,
            SourceTemplateId = template.Id,
            Date = January.AddDays(2),
            ScheduledDate = January.AddDays(1),
            Memo = "Edited posting",
            Splits = template.Splits.Select(item => item with { Id = Guid.NewGuid() }).ToArray(),
        };
        plan = plan with { Transactions = [template, existing] };
        var known = new HashSet<RecurringOccurrence> { new(template.Id, January), new(Guid.NewGuid(), January.AddDays(2)) };
        var originalKnown = known.ToArray();
        PlanCommand command = save ? new SaveTransaction(0, template) : new PostRecurring(0, January.AddDays(2));

        var outcome = PlanCommandHandler.Apply(NewIds(), plan, command, January.AddDays(2), posted: known);
        outcome.IsT0.ShouldBeTrue();
        var result = outcome.AsT0;

        result.Transactions.Count.ShouldBe(3);
        result.Transactions.Single(item => item.Id == existing.Id).ShouldBe(existing);
        var created = result.Transactions.Single(item => item.Id != template.Id && item.Id != existing.Id);
        created.ScheduledDate.ShouldBe(January.AddDays(2));
        created.Amount.ShouldBe(-25);
        created.NeedsApproval.ShouldBeTrue();
        result.Transactions.Single(item => item.Id == template.Id).Date.ShouldBe(January.AddDays(3));
        plan.Transactions.ShouldBe([template, existing]);
        known.ShouldBe(originalKnown, ignoreOrder: true);
    }

    [Fact]
    public void AlreadyPostedDatesConsumeTheBatchLimitWithoutAllocatingIdentities()
    {
        var plan = Create();
        var template = Entry(plan, 0, -25, date: January) with { Repeat = RepeatFrequency.Daily };
        plan = plan with { Transactions = [template] };
        var known = Enumerable.Range(0, 128).Select(day => new RecurringOccurrence(template.Id, January.AddDays(day))).ToHashSet();

        var result = TransactionChanges.PostDue(new([]), plan, January.AddDays(200), known);

        var next = result.Transactions.Single();
        next.Id.ShouldBe(template.Id);
        next.Date.ShouldBe(January.AddDays(128));
        next.Occurrence.ShouldBe(128);
        known.Count.ShouldBe(128);
    }

    [Fact]
    public void RejectsOneOffFutureEntriesButAcceptsRepeatingInstructions()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -25, date: plan.Today.AddDays(1));
        TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, entry), plan.Today).IsT1.ShouldBeTrue();
        var result = TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, entry with { Repeat = RepeatFrequency.Monthly }), plan.Today);
        result.IsT0.ShouldBeTrue();
        result.AsT0.Transactions.Single().AnchorDate.ShouldBe(entry.Date);
    }

    [Fact]
    public void DueRecurrencesArePostedOnceWithApprovalAndAnchorDayPreserved()
    {
        var plan = Create();
        var anchor = January.AddDays(30);
        var template = Entry(plan, 0, -25, date: anchor) with { Repeat = RepeatFrequency.Monthly, AnchorDate = anchor };
        plan = plan with { Transactions = [template] };
        var posted = TransactionChanges.PostDue(BudgetTestData.NewIds(), plan, new(2026, 3, 31));
        var occurrences = posted.Transactions.Where(item => item.SourceTemplateId == template.Id).OrderBy(item => item.Date).ToArray();
        occurrences.Select(item => item.Date).ShouldBe([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31)]);
        occurrences.ShouldAllBe(item => item.NeedsApproval && item.State == ClearingState.Uncleared && item.Amount == -25);
        posted.Transactions.Single(item => item.Id == template.Id).Date.ShouldBe(new(2026, 4, 30));
        TransactionChanges.PostDue(BudgetTestData.NewIds(), posted, new(2026, 3, 31)).Transactions.Count.ShouldBe(4);
        occurrences.SelectMany(item => item.Splits).Select(item => item.Id).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void TransferClearingUpdatesOnlyTheSelectedAccountSide()
    {
        var plan = Create();
        var transfer = Entry(plan, 0, -100) with { TransferAccountId = plan.Accounts[1].Id, Splits = [] };
        plan = plan with { Transactions = [transfer] };
        var result = TransactionChanges.UpdateStates(plan, new(0, [transfer.Id], ClearingState.Cleared, false, plan.Accounts[1].Id));
        result.IsT0.ShouldBeTrue();
        result.AsT0.Transactions.Single().State.ShouldBe(ClearingState.Uncleared);
        result.AsT0.Transactions.Single().TransferState.ShouldBe(ClearingState.Cleared);
        BudgetFacts.Balance(result.AsT0, plan.Accounts[1], plan.Today).Cleared.ShouldBe(100);
        BudgetFacts.Balance(result.AsT0, plan.Accounts[0], plan.Today).Uncleared.ShouldBe(-100);
    }

    [Fact]
    public void EditingTemplateDetailsPreservesTheOriginalMonthEndAnchor()
    {
        var plan = Create();
        var template = Entry(plan, 0, -25, date: new(2026, 1, 31)) with { Repeat = RepeatFrequency.Monthly };
        plan = TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, template), new(2026, 1, 31)).AsT0;
        var next = plan.Transactions.Single(item => item.Repeat != RepeatFrequency.None);
        next.Date.ShouldBe(new(2026, 2, 28));
        var edited = next with
        {
            Memo = "Updated subscription",
            Amount = -30,
            Splits = [next.Splits[0] with { Amount = -30 }],
            AnchorDate = null,
            Occurrence = 0,
        };

        var result = TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, edited), new(2026, 2, 1));

        result.IsT0.ShouldBeTrue();
        var saved = result.AsT0.Transactions.Single(item => item.Id == next.Id);
        saved.AnchorDate.ShouldBe(new DateOnly(2026, 1, 31));
        saved.Occurrence.ShouldBe(1);
        var posted = TransactionChanges.PostDue(BudgetTestData.NewIds(), result.AsT0, new(2026, 3, 31));
        posted.Transactions.Where(item => item.SourceTemplateId == next.Id).OrderBy(item => item.Date)
            .Select(item => (item.Date, item.Amount)).ShouldBe([(new(2026, 1, 31), -25m), (new(2026, 2, 28), -30m), (new(2026, 3, 31), -30m)]);
    }

    [Theory]
    [InlineData(27, RepeatFrequency.Monthly, 2026, 3, 27)]
    [InlineData(28, RepeatFrequency.Weekly, 2026, 3, 7)]
    public void ChangingTemplateScheduleEstablishesANewAnchor(int day, RepeatFrequency frequency, int year, int month, int nextDay)
    {
        var plan = Create();
        var template = Entry(plan, 0, -25, date: new(2026, 1, 31)) with { Repeat = RepeatFrequency.Monthly };
        plan = TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, template), new(2026, 1, 31)).AsT0;
        var next = plan.Transactions.Single(item => item.Repeat != RepeatFrequency.None);
        var date = new DateOnly(2026, 2, day);

        var result = TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, next with { Date = date, Repeat = frequency }), date);

        result.IsT0.ShouldBeTrue();
        var saved = result.AsT0.Transactions.Single(item => item.Id == next.Id);
        saved.AnchorDate.ShouldBe(date);
        saved.Date.ShouldBe(new(year, month, nextDay));
        saved.Occurrence.ShouldBe(1);
    }

    [Fact]
    public void InvalidSplitTotalsAndForeignCategoryAreRejected()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -100);
        TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, entry with { Amount = -99 }), plan.Today).IsT1.ShouldBeTrue();
        TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, entry with { Splits = [entry.Splits[0] with { CategoryId = Guid.NewGuid() }] }), plan.Today).IsT1.ShouldBeTrue();
        TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, entry with { Splits = [entry.Splits[0] with { Amount = decimal.MaxValue }, entry.Splits[0]] }), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void EditingOccurrenceDatePreservesItsScheduledIdentityAndPreventsReposting()
    {
        var plan = Create();
        var template = Entry(plan, 0, -25) with { Repeat = RepeatFrequency.Daily };
        plan = TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, template), January.AddDays(2)).AsT0;
        var occurrence = plan.Transactions.Single(item => item.SourceTemplateId == template.Id && item.Date == January.AddDays(1));

        var result = TransactionChanges.Save(BudgetTestData.NewIds(), plan, new(0, occurrence with { Date = January.AddDays(2), ScheduledDate = null }), plan.Today);

        result.IsT0.ShouldBeTrue();
        var saved = result.AsT0.Transactions.Single(item => item.Id == occurrence.Id);
        saved.Date.ShouldBe(January.AddDays(2));
        saved.ScheduledDate.ShouldBe(January.AddDays(1));
        result.AsT0.Transactions.Count(item => item.SourceTemplateId == template.Id && item.Date == saved.Date).ShouldBe(2);
        // Rewind the template deliberately: catch-up must recognize the edited occurrence.
        var next = result.AsT0.Transactions.Single(item => item.Id == template.Id);
        var replayed = TransactionChanges.Save(BudgetTestData.NewIds(), result.AsT0, new(0, next with { Date = January.AddDays(1) }), January.AddDays(2)).AsT0;
        replayed.Transactions.Count.ShouldBe(3);
        replayed.Transactions.Single(item => item.Id == occurrence.Id).ShouldBe(saved);
    }

    [Fact]
    public void ReconciliationLocksOnlyClearedEntriesAndRequiresExplicitAdjustment()
    {
        var plan = Create();
        var cleared = Entry(plan, 0, -100) with { State = ClearingState.Cleared };
        var pending = Entry(plan, 0, -50);
        plan = plan with { Transactions = [cleared, pending] };
        var rejected = MoneyChanges.Reconcile(BudgetTestData.NewIds(), plan, new(0, plan.Accounts[0].Id, plan.Today, 890, false), plan.Today);
        rejected.IsT1.ShouldBeTrue();
        rejected.AsT1.Message.ShouldContain("$10.00", Case.Insensitive);
        var result = MoneyChanges.Reconcile(BudgetTestData.NewIds(), plan, new(0, plan.Accounts[0].Id, plan.Today, 890, true), plan.Today);
        result.IsT0.ShouldBeTrue();
        var updated = result.AsT0;
        updated.Transactions.Single(item => item.Id == cleared.Id).State.ShouldBe(ClearingState.Reconciled);
        updated.Transactions.Single(item => item.Id == pending.Id).State.ShouldBe(ClearingState.Uncleared);
        updated.Transactions.Single(item => string.Equals(item.Payee, "Reconciliation adjustment", StringComparison.Ordinal)).Amount.ShouldBe(-10);
        BudgetFacts.Balance(updated, plan.Accounts[0], plan.Today).Cleared.ShouldBe(890);
        TransactionChanges.Delete(updated, new(0, [cleared.Id])).IsT1.ShouldBeTrue();
        TransactionChanges.Save(BudgetTestData.NewIds(), updated, new(0, cleared), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void ReconciliationRejectsAnAdjustmentBeyondTheSupportedAmount()
    {
        var plan = Create();
        plan = plan with { Accounts = [plan.Accounts[0] with { OpeningBalance = -AmountExpression.MaximumAmount }, plan.Accounts[1]] };
        var result = MoneyChanges.Reconcile(BudgetTestData.NewIds(), plan, new(0, plan.Accounts[0].Id, plan.Today, AmountExpression.MaximumAmount, true), plan.Today);
        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("exceeds the supported transaction amount");
        plan.Transactions.ShouldBeEmpty();
    }

    [Fact]
    public void MovingMoneyChangesAssignmentsWithoutChangingCashOrTotalAssigned()
    {
        var plan = Assigned(Create(), 300);
        var result = MoneyChanges.Move(plan, new(0, plan.Categories[0].Id, plan.Categories[1].Id, January, 125), plan.Today);
        result.IsT0.ShouldBeTrue();
        var month = BudgetCalculator.Calculate(result.AsT0, January, plan.Today);
        month.Assigned.ShouldBe(300);
        month.Categories[0].Available.ShouldBe(175);
        month.Categories[1].Available.ShouldBe(125);
        month.ReadyToAssign.ShouldBe(700);
        MoneyChanges.Move(plan, new(0, plan.Categories[0].Id, null, January, 301), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void ClosingAnAccountRequiresZeroBalanceAndOpeningChangesRespectReconciliation()
    {
        var plan = Create();
        CatalogChanges.Save(BudgetTestData.NewIds(), plan, new(0, plan.Accounts[0] with { Closed = true }), plan.Today).IsT1.ShouldBeTrue();
        plan = plan with { Transactions = [Entry(plan, 0, -50) with { State = ClearingState.Reconciled }] };
        CatalogChanges.Save(BudgetTestData.NewIds(), plan, new(0, plan.Accounts[0] with { OpeningBalance = 1100 }), plan.Today).IsT1.ShouldBeTrue();
        CatalogChanges.Save(BudgetTestData.NewIds(), plan, new(0, plan.Accounts[0] with { Name = "New name" }), plan.Today).IsT0.ShouldBeTrue();
    }
}
