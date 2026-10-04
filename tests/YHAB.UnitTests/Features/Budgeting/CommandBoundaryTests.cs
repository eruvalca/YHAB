using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class CommandBoundaryTests
{
    [Theory]
    [InlineData(AccountKind.Checking, AccountKind.Savings, -10, false)]
    [InlineData(AccountKind.Asset, AccountKind.Checking, -10, true)]
    [InlineData(AccountKind.Asset, AccountKind.Checking, 10, true)]
    [InlineData(AccountKind.Checking, AccountKind.Asset, -10, true)]
    [InlineData(AccountKind.Asset, AccountKind.Asset, -10, false)]
    public void TransferValidationUsesTheBudgetSideAndAcceptsOnlyItsRequiredSplits(AccountKind source, AccountKind destination, decimal amount, bool categorized)
    {
        var plan = Create();
        plan = plan with { Accounts = [plan.Accounts[0] with { Kind = source }, plan.Accounts[1] with { Kind = destination }] };
        var entry = Entry(plan, 0, amount) with { TransferAccountId = plan.Accounts[1].Id };
        if (!categorized) { entry = entry with { Splits = [] }; }
        var result = TransactionChanges.Save(NewIds(), plan, new(0, entry), plan.Today).AsT0;
        result.Transactions.Single().Amount.ShouldBe(amount);
        result.Transactions.Single().Splits.ShouldBe(entry.Splits);
        BudgetFacts.Balance(result, result.Accounts[0], plan.Today).Working.ShouldBe(1000 + amount);
        BudgetFacts.Balance(result, result.Accounts[1], plan.Today).Working.ShouldBe(-amount);
        var wrong = categorized ? entry with { Splits = [] } : entry with { Splits = [new(Guid.Empty, plan.Categories[0].Id, amount, "")] };
        TransactionChanges.Save(NewIds(), plan, new(0, wrong), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void ExistingCatalogEditsKeepIdentitiesAndAcceptValidDatedTargets()
    {
        var plan = Create();
        var group = plan.Groups[0] with { Name = " Renamed " };
        var edited = CatalogChanges.Save(NewIds(), plan, new SaveGroup(0, group)).AsT0;
        edited.Groups.Single().ShouldBe(group with { Name = "Renamed" });
        foreach (var cadence in new[] { TargetCadence.Monthly, TargetCadence.Yearly })
        {
            var target = new TargetData(TargetKind.Refill, cadence, 1200, January, January.AddMonths(11), 12);
            var category = plan.Categories[0] with { Target = target };
            edited = CatalogChanges.Save(NewIds(), edited, new SaveCategory(0, category)).AsT0;
            edited.Categories.Single(item => item.Id == category.Id).ShouldBe(category);
            edited.Categories.Count.ShouldBe(2);
        }
        var payment = plan.Categories[1];
        CatalogChanges.Save(NewIds(), plan, new SaveCategory(0, payment with { CreditAccountId = null })).IsT1.ShouldBeTrue();
        CatalogChanges.Save(NewIds(), plan, new SaveCategory(0, payment with { Notes = "Statement" })).AsT0.Categories.Single(item => item.Id == payment.Id).Notes.ShouldBe("Statement");
        var transfer = Entry(plan, 1, -10) with { TransferAccountId = plan.Accounts[0].Id, Splits = [] };
        plan = plan with { Transactions = [transfer] };
        CatalogChanges.Save(NewIds(), plan, new SaveAccount(0, plan.Accounts[0] with { OpenedOn = January.AddDays(2) }), plan.Today).IsT1.ShouldBeTrue();
        CatalogChanges.Save(NewIds(), plan, new SaveAccount(0, plan.Accounts[0] with { OpenedOn = transfer.Date }), plan.Today).IsT0.ShouldBeTrue();
    }

    [Fact]
    public void StatusOnlyApprovalKeepsEachTransferSideAndRejectsAnUnrelatedAccount()
    {
        var plan = Create();
        var entry = Entry(plan, 0, -10) with { TransferAccountId = plan.Accounts[1].Id, TransferState = ClearingState.Cleared, NeedsApproval = true, Splits = [] };
        plan = plan with { Transactions = [entry] };
        var command = new UpdateTransactionStates(0, [entry.Id], null, true, plan.Accounts[1].Id);
        var result = TransactionChanges.UpdateStates(plan, command).AsT0.Transactions.Single();
        result.ShouldBe(entry with { NeedsApproval = false });
        TransactionChanges.UpdateStates(plan, command with { AccountId = Guid.NewGuid() }).IsT1.ShouldBeTrue();
        TransactionChanges.UpdateStates(plan, command with { AccountId = plan.Accounts[0].Id, State = ClearingState.Cleared }).AsT0.Transactions.Single().State.ShouldBe(ClearingState.Cleared);
    }

    [Fact]
    public void LegacyRecurringMetadataAndApprovalPreserveOccurrenceIdentity()
    {
        var plan = Create();
        var template = Entry(plan, 0, -10) with { Repeat = RepeatFrequency.Monthly, AnchorDate = null };
        var occurrence = template with { Id = Guid.NewGuid(), Repeat = RepeatFrequency.None, SourceTemplateId = template.Id, Splits = [template.Splits[0] with { Id = Guid.NewGuid() }], NeedsApproval = true };
        plan = plan with { Transactions = [template, occurrence] };
        var command = new SaveTransaction(0, template with { Memo = "Keep schedule" });
        var result = TransactionChanges.Save(CommandIds.Allocate(plan, command), plan, command, plan.Today).AsT0;
        result.Transactions.Count.ShouldBe(2);
        result.Transactions.Single(item => item.Id == template.Id).AnchorDate.ShouldBe(template.Date);
        var edited = TransactionChanges.Save(NewIds(), result, new(0, occurrence with { Memo = "Legacy occurrence" }), plan.Today).AsT0;
        edited.Transactions.Single(item => item.Id == occurrence.Id).ScheduledDate.ShouldBe(occurrence.Date);
        var approved = TransactionChanges.UpdateStates(edited, new(0, [occurrence.Id], null, true, null)).AsT0;
        approved.Transactions.Single(item => item.Id == occurrence.Id).NeedsApproval.ShouldBeFalse();
        approved.Transactions.Single(item => item.Id == template.Id).Memo.ShouldBe("Keep schedule");
    }

    [Fact]
    public void CategoryMergePreservesUnrelatedSplitsAndRejectsSelfReplacement()
    {
        var plan = Create();
        var category = plan.Categories[0];
        var other = category with { Id = Guid.NewGuid(), Name = "Other" };
        var entry = Entry(plan, 0, -20) with { Splits = [new(Guid.NewGuid(), other.Id, -10, "Retain"), new(Guid.NewGuid(), category.Id, -10, "Move")] };
        plan = plan with { Categories = [.. plan.Categories, other], Transactions = [entry] };
        CatalogChanges.Remove(plan, new(0, category.Id, category.Id)).IsT1.ShouldBeTrue();
        var merged = CatalogChanges.Remove(plan, new(0, category.Id, other.Id)).AsT0;
        merged.Transactions.Single().Splits[0].ShouldBe(entry.Splits[0]);
        merged.Transactions.Single().Splits[1].ShouldBe(entry.Splits[1] with { CategoryId = other.Id });
        var target = category with { Target = new(TargetKind.Balance, TargetCadence.Monthly, 100, January, null), GroupId = Guid.NewGuid() };
        plan = plan with { Categories = [target], Groups = [plan.Groups[0], new(target.GroupId, "Hidden", 1, true)], Transactions = [] };
        MoneyChanges.AutoAssign(plan, new(0, January), plan.Today).AsT0.Allocations.ShouldBeEmpty();
        MoneyChanges.Move(plan, new(0, null, null, January, 1), plan.Today).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void AutoAssignHonorsTheMaximumAndCanCreateAnAllocationFromReadyMoney()
    {
        var plan = Create(cash: AmountExpression.MaximumAmount);
        var category = plan.Categories[0] with { Target = new(TargetKind.Balance, TargetCadence.Monthly, AmountExpression.MaximumAmount, January, null) };
        plan = plan with { Categories = [category, plan.Categories[1]] };
        var first = MoneyChanges.AutoAssign(plan, new(0, January), plan.Today).AsT0;
        first.Allocations.Single().Amount.ShouldBe(AmountExpression.MaximumAmount);
        first = first with { Transactions = [Entry(first, 0, -10)] };
        MoneyChanges.AutoAssign(first, new(0, January), plan.Today).AsT0.Allocations.Single().Amount.ShouldBe(AmountExpression.MaximumAmount);
        MoneyChanges.Move(plan, new(0, null, category.Id, new(2101, 1, 1), 1), plan.Today).IsT1.ShouldBeTrue();
    }
}
