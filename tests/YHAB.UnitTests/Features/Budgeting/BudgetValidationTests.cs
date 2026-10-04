using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;
using static YHAB.UnitTests.Features.Budgeting.BudgetTestData;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BudgetValidationTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("blank-name")]
    [InlineData("long-name")]
    [InlineData("kind")]
    [InlineData("notes")]
    [InlineData("long-notes")]
    [InlineData("precision")]
    [InlineData("maximum")]
    [InlineData("negative-minimum")]
    [InlineData("minimum-precision")]
    [InlineData("negative-rate")]
    [InlineData("rate")]
    [InlineData("old-date")]
    [InlineData("future-date")]
    [InlineData("change-type")]
    public void InvalidAccountsCannotChangeThePlan(string scenario)
    {
        var plan = Create();
        var account = plan.Accounts[0];
        account = scenario switch
        {
            "missing" => null!,
            "blank-name" => account with { Name = " " },
            "long-name" => account with { Name = new string('a', 101) },
            "kind" => account with { Kind = (AccountKind)999 },
            "notes" => account with { Notes = null! },
            "long-notes" => account with { Notes = new string('x', 4001) },
            "precision" => account with { OpeningBalance = 0.001m },
            "maximum" => account with { OpeningBalance = AmountExpression.MaximumAmount + 1 },
            "negative-minimum" => account with { MinimumPayment = -1 },
            "minimum-precision" => account with { MinimumPayment = 0.001m },
            "negative-rate" => account with { InterestRate = -1 },
            "rate" => account with { InterestRate = 101 },
            "old-date" => account with { OpenedOn = new(1999, 12, 31) },
            "future-date" => account with { OpenedOn = plan.Today.AddDays(1) },
            _ => account with { Kind = AccountKind.Savings },
        };
        Reject(plan, new SaveAccount(0, account));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("name")]
    [InlineData("notes")]
    [InlineData("long-notes")]
    [InlineData("group")]
    [InlineData("payment")]
    [InlineData("kind")]
    [InlineData("cadence")]
    [InlineData("weekday")]
    [InlineData("zero")]
    [InlineData("precision")]
    [InlineData("start-date")]
    [InlineData("start-day")]
    [InlineData("negative-repeat")]
    [InlineData("repeat")]
    [InlineData("due-before-start")]
    [InlineData("due-date")]
    [InlineData("yearly-undated")]
    public void InvalidCategoriesAndTargetsCannotChangeThePlan(string scenario)
    {
        var plan = Create();
        var target = new TargetData(TargetKind.Refill, TargetCadence.Monthly, 100, January, null);
        target = scenario switch
        {
            "kind" => target with { Kind = (TargetKind)99 },
            "cadence" => target with { Cadence = (TargetCadence)99 },
            "weekday" => target with { Weekday = (DayOfWeek)99 },
            "zero" => target with { Amount = 0 },
            "precision" => target with { Amount = 0.001m },
            "start-date" => target with { StartMonth = new(1999, 1, 1) },
            "start-day" => target with { StartMonth = January.AddDays(1) },
            "negative-repeat" => target with { RepeatEveryMonths = -1 },
            "repeat" => target with { RepeatEveryMonths = 121 },
            "due-before-start" => target with { DueDate = January.AddDays(-1) },
            "due-date" => target with { DueDate = new(2101, 1, 1) },
            "yearly-undated" => target with { Cadence = TargetCadence.Yearly },
            _ => target,
        };
        var category = plan.Categories[0] with { Target = target };
        category = scenario switch
        {
            "missing" => null!,
            "name" => category with { Name = "" },
            "notes" => category with { Notes = null! },
            "long-notes" => category with { Notes = new string('a', 4001) },
            "group" => category with { GroupId = Guid.NewGuid() },
            "payment" => category with { CreditAccountId = plan.Accounts[1].Id },
            _ => category,
        };
        Reject(plan, new SaveCategory(0, category));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("payee")]
    [InlineData("memo")]
    [InlineData("flag")]
    [InlineData("clearing")]
    [InlineData("transfer-state")]
    [InlineData("repeat")]
    [InlineData("missing-account")]
    [InlineData("closed")]
    [InlineData("before-opening")]
    [InlineData("same-account")]
    [InlineData("unknown-transfer")]
    [InlineData("before-transfer-opening")]
    [InlineData("tracking-splits")]
    [InlineData("old-recurring")]
    [InlineData("reconciled")]
    [InlineData("reconciled-transfer")]
    [InlineData("empty-splits")]
    [InlineData("duplicate-split")]
    [InlineData("owned-split")]
    [InlineData("split-memo")]
    [InlineData("uncategorized-outflow")]
    public void InvalidTransactionsCannotChangeThePlan(string scenario)
    {
        var plan = Create();
        var entry = Entry(plan, 0, -10);
        if (string.Equals(scenario, "closed", StringComparison.Ordinal)) { plan = plan with { Accounts = [plan.Accounts[0] with { Closed = true }, plan.Accounts[1]] }; }
        if (string.Equals(scenario, "tracking-splits", StringComparison.Ordinal)) { plan = plan with { Accounts = [plan.Accounts[0] with { Kind = AccountKind.Asset }, plan.Accounts[1]] }; }
        if (string.Equals(scenario, "owned-split", StringComparison.Ordinal)) { plan = plan with { Transactions = [entry with { Id = Guid.NewGuid() }] }; }
        if (string.Equals(scenario, "old-recurring", StringComparison.Ordinal)) { plan = plan with { Accounts = [plan.Accounts[0] with { OpenedOn = January.AddYears(-10) }, plan.Accounts[1]] }; }
        if (string.Equals(scenario, "before-transfer-opening", StringComparison.Ordinal)) { plan = plan with { Accounts = [plan.Accounts[0], plan.Accounts[1] with { OpenedOn = plan.Today }] }; }
        entry = scenario switch
        {
            "missing" => null!,
            "payee" => entry with { Payee = null! },
            "memo" => entry with { Memo = new string('a', 4001) },
            "flag" => entry with { Flag = "Pink" },
            "clearing" => entry with { State = (ClearingState)99 },
            "transfer-state" => entry with { TransferState = (ClearingState)99 },
            "repeat" => entry with { Repeat = (RepeatFrequency)99 },
            "missing-account" => entry with { AccountId = Guid.NewGuid() },
            "before-opening" => entry with { Date = January.AddDays(-1) },
            "same-account" => entry with { TransferAccountId = entry.AccountId },
            "unknown-transfer" => entry with { TransferAccountId = Guid.NewGuid() },
            "before-transfer-opening" => entry with { TransferAccountId = plan.Accounts[1].Id },
            "old-recurring" => entry with { Date = January.AddYears(-6), Repeat = RepeatFrequency.Monthly },
            "reconciled" => entry with { State = ClearingState.Reconciled },
            "reconciled-transfer" => entry with { TransferState = ClearingState.Reconciled },
            "empty-splits" => entry with { Splits = [] },
            "duplicate-split" => entry with { Amount = -20, Splits = [entry.Splits[0], entry.Splits[0]] },
            "split-memo" => entry with { Splits = [entry.Splits[0] with { Memo = new string('a', 1001) }] },
            "uncategorized-outflow" => entry with { Splits = [entry.Splits[0] with { CategoryId = null }] },
            _ => entry,
        };
        Reject(plan, new SaveTransaction(0, entry));
    }

    [Fact]
    public void GroupAndCategoryCreationAndRemovalKeepTheCatalogConsistent()
    {
        var plan = Create();
        Reject(plan, new SaveGroup(0, null!));
        Reject(plan, new SaveGroup(0, new(Guid.Empty, " ", 0)));
        var group = CatalogChanges.Save(BudgetTestData.NewIds(), plan, new SaveGroup(0, new(Guid.Empty, "  Travel  ", 2))).AsT0;
        var createdGroup = group.Groups.Single(item => string.Equals(item.Name, "Travel", StringComparison.Ordinal));
        createdGroup.Id.Version.ShouldBe(7);
        var added = CatalogChanges.Save(BudgetTestData.NewIds(), group, new SaveCategory(0, new(Guid.Empty, createdGroup.Id, "Trips", "", 0, false, null, null))).AsT0;
        var category = added.Categories.Single(item => string.Equals(item.Name, "Trips", StringComparison.Ordinal));
        category.Id.Version.ShouldBe(7);
        CatalogChanges.Remove(added, new(0, category.Id, null)).AsT0.Categories.ShouldNotContain(item => item.Id == category.Id);
        Reject(plan, new RemoveCategory(0, Guid.NewGuid(), null));
        Reject(plan, new RemoveCategory(0, plan.Categories[1].Id, null));
        var metadata = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, new UpdatePlan(0, " Renamed ", "Notes"), plan.Today).AsT0;
        metadata.Name.ShouldBe("Renamed");
        metadata.Notes.ShouldBe("Notes");
        Reject(plan, new UpdatePlan(0, "", ""));
        Reject(plan, new RenamePayee(0, "", "New"));
        Reject(plan, new PostRecurring(0, plan.Today.AddDays(1)));
    }

    private static void Reject(PlanSnapshot plan, PlanCommand command)
    {
        var original = JsonSerializer.Serialize(plan);
        var result = PlanCommandHandler.Apply(BudgetTestData.NewIds(), plan, command, plan.Today);
        result.IsT1.ShouldBeTrue($"Expected invalid {command.GetType().Name}");
        result.AsT1.Message.ShouldNotBeNullOrWhiteSpace();
        JsonSerializer.Serialize(plan).ShouldBe(original);
    }
}
