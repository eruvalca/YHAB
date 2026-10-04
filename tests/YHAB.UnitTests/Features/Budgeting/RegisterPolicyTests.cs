using System.Diagnostics.CodeAnalysis;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class RegisterPolicyTests
{
    [Theory]
    [InlineData("All posted", "Newest first", 1)]
    [InlineData("Needs approval", "Oldest first", 100)]
    [InlineData("Uncleared", "Payee", 50)]
    [InlineData("Cleared", "Amount", 50)]
    [InlineData("Reconciled", "Amount", 50)]
    [InlineData("Recurring", "Newest first", 50)]
    public void SupportedFiltersAndBoundaryPageSizesAreAccepted(string filter, string sort, int size)
        => Should.NotThrow(() => RegisterPolicy.Validate(new(Filter: filter, Sort: sort, PageSize: size)));

    [Fact]
    public void MalformedQueriesAreRejectedBeforeReadingTheLedger()
    {
        RegisterQuery[] invalid = [new(PageSize: 0), new(PageSize: 101), new(Search: null!), new(Search: new string('x', 201)),
            new(From: new(2026, 2, 1), Through: new(2026, 1, 1)), new(Sort: "unknown"), new(Filter: "unknown")];
        foreach (var query in invalid)
        {
            Should.Throw<BudgetRequestException>(() => RegisterPolicy.Validate(query)).Status.ShouldBe(400);
        }
        Should.Throw<ArgumentNullException>(() => RegisterPolicy.Validate(null!));
    }
}
