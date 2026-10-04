using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Shouldly;
using Xunit;
using YHAB.Features.Budgeting.Data;
using YHAB.Features.Budgeting.Models;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PayeePatchTests
{
    [Theory]
    [InlineData("Market", "market", true)]
    [InlineData("Café", "CAFÉ", true)]
    [InlineData("Cafe", "Café", false)]
    [InlineData("%_\\", "%_\\", true)]
    [InlineData("Already", "already", false)]
    public void RenameStoresOnlyChangedNamesAndPreservesInputs(string original, string selected, bool changes)
    {
        var value = new TransactionPayee(Guid.NewGuid(), original);
        var other = new TransactionPayee(Guid.NewGuid(), "Unrelated");
        TransactionPayee[] current = [value, other];
        var command = new RenamePayee(0, selected, " Already ");
        PayeePatch.ValidationError(command).ShouldBeNull();

        var patch = PayeePatch.Rename(current, command);

        current.ShouldBe([value, other]);
        patch.Before.ShouldBe(changes ? [value] : []);
        patch.After.ShouldBe(changes ? [value with { Payee = "Already" }] : []);
        ((BudgetMutation)patch).IsEmpty.ShouldBe(!changes);
        patch.Reverse().Before.ShouldBe(patch.After);
        patch.Reverse().After.ShouldBe(patch.Before);
    }

    [Theory]
    [InlineData(null, "New")]
    [InlineData("", "New")]
    [InlineData("  ", "New")]
    [InlineData("Old", null)]
    [InlineData("Old", "")]
    [InlineData("Old", " \t ")]
    public void MissingNamesAreRejected(string? oldName, string? newName)
        => PayeePatch.ValidationError(new(0, oldName!, newName!)).ShouldNotBeNull();

    [Fact]
    public void NameLimitIsCheckedBeforeTrimming()
    {
        PayeePatch.ValidationError(new(0, "Old", new string('n', 200))).ShouldBeNull();
        PayeePatch.ValidationError(new(0, "Old", new string('n', 201))).ShouldNotBeNull();
        PayeePatch.ValidationError(new(0, "Old", new string('n', 200) + " ")).ShouldNotBeNull();
    }

    [Fact]
    public void RestoreRequiresEveryExpectedIdentityAndExactNameButAllowsUnrelatedEntries()
    {
        var value = new TransactionPayee(Guid.NewGuid(), "Before");
        var after = value with { Payee = "After" };
        var unrelated = new TransactionPayee(Guid.NewGuid(), "Other");
        var patch = new PayeePatch([value], [after]);
        patch.CanApply([value, unrelated]).ShouldBeTrue();
        patch.Reverse().CanApply([after, unrelated]).ShouldBeTrue();
        patch.CanApply([]).ShouldBeFalse();
        patch.CanApply([value with { Payee = "Later change" }]).ShouldBeFalse();
        patch.CanApply([value with { Payee = "before" }]).ShouldBeFalse();
        new PayeePatch([value], []).CanApply([value]).ShouldBeFalse();
        new PayeePatch([value], [after with { Id = Guid.NewGuid() }]).CanApply([value]).ShouldBeFalse();
        new PayeePatch([], []).CanApply([unrelated]).ShouldBeTrue();
    }

    [Fact]
    public void PayeeHistoryRoundTripsWithoutTransactionSnapshots()
    {
        var names = new[] { new TransactionPayee(Guid.NewGuid(), "market"), new TransactionPayee(Guid.NewGuid(), "MARKET") };
        var patch = PayeePatch.Rename(names, new(0, "Market", "New"));
        var encoded = BudgetHistoryCodec.Serialize(patch);
        encoded.Before.ShouldContain("payee-names-v1");
        encoded.Before.ShouldNotContain("accountId");
        var decoded = BudgetHistoryCodec.Deserialize(new BudgetHistory { Before = encoded.Before, After = encoded.After });
        decoded.IsT1.ShouldBeTrue();
        decoded.AsT1.Before.ShouldBe(names);
        decoded.AsT1.After.ShouldBe(names.Select(item => item with { Payee = "New" }).ToArray());
    }

    [Fact]
    public void LedgerHistoryRetainsItsExistingFormatAndSparseValues()
    {
        var before = BudgetTestData.Create();
        var after = before with { Name = "Renamed plan" };
        var patch = LedgerPatch.Between(before, after);
        ((BudgetMutation)patch).IsEmpty.ShouldBeFalse();
        ((BudgetMutation)LedgerPatch.Between(before, before)).IsEmpty.ShouldBeTrue();
        var encoded = BudgetHistoryCodec.Serialize(patch);
        encoded.Before.ShouldNotContain("payee-names-v1");
        var decoded = BudgetHistoryCodec.Deserialize(new BudgetHistory { Before = encoded.Before, After = encoded.After });
        decoded.IsT0.ShouldBeTrue();
        decoded.AsT0.TryApply(before, reverse: false, out var restored).ShouldBeTrue();
        restored.Name.ShouldBe("Renamed plan");
        restored.Accounts.ShouldBe(before.Accounts);
    }

    [Theory]
    [InlineData("null", "{}")]
    [InlineData("{}", "[]")]
    [InlineData("{\"kind\":\"future\"}", "{}")]
    [InlineData("{\"kind\":\"payee-names-v1\"}", "{}")]
    [InlineData("{\"kind\":\"payee-names-v1\"}", "{\"kind\":\"future\"}")]
    [InlineData("{\"kind\":\"payee-names-v1\"}", "{\"kind\":\"payee-names-v1\",\"values\":[]}")]
    [InlineData("{\"kind\":\"payee-names-v1\",\"values\":[]}", "{\"kind\":\"payee-names-v1\"}")]
    [InlineData("{}", "{\"kind\":\"payee-names-v1\"}")]
    public void UnknownOrIncompleteHistoryFailsBeforeAnyRestore(string before, string after)
        => Should.Throw<InvalidOperationException>(() => BudgetHistoryCodec.Deserialize(new BudgetHistory { Before = before, After = after }));

    [Fact]
    public void MalformedHistoryPreservesItsParsingFailure()
        => Should.Throw<JsonException>(() => BudgetHistoryCodec.Deserialize(new BudgetHistory { Before = "{", After = "{}" }));
}
