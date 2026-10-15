using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class AmountExpressionTests
{
    [Theory]
    [InlineData("150 + 25", "175")]
    [InlineData("+50 - 25 * +2", "0")]
    [InlineData("10-3-2", "5")]
    [InlineData("2 + 3 * 4", "14")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("$1,200.50 / 2", "600.25")]
    [InlineData("-(3 + 2)", "-5")]
    [InlineData("10 / 3", "3.33")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("999999999.99", "999999999.99")]
    public void EvaluatesBoundedDecimalArithmetic(string expression, string expected)
    {
        AmountExpression.TryEvaluate(expression, out var value).ShouldBeTrue();
        value.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("12,50", "12.50")]
    [InlineData("1,5", "1.5")]
    [InlineData(",50", "0.50")]
    [InlineData("1234,56", "1234.56")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("12,50 + 3,25", "12.50 + 3.25")]
    [InlineData("-12,50", "-12.50")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1,234", "1234")]
    [InlineData("1,000,000", "1000000")]
    [InlineData("$1,200.50", "1200.50")]
    public void ReadsCommaDecimalsTheSameAsDottedDecimals(string commaForm, string dottedForm)
    {
        AmountExpression.TryEvaluate(commaForm, out var fromComma).ShouldBeTrue();
        AmountExpression.TryEvaluate(dottedForm, out var fromDot).ShouldBeTrue();
        fromComma.ShouldBe(fromDot);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("1/0")]
    [InlineData("(1+2")]
    [InlineData("1;alert(1)")]
    [InlineData("1e30")]
    [InlineData("1000000000")]
    [InlineData("79228162514264337593543950335*2")]
    [InlineData("1 +")]
    [InlineData("1..234,56")]
    [InlineData("1.2.3,45")]
    [InlineData("1.2345,67")]
    [InlineData("1,234,56")]
    [InlineData("1,2.")]
    public void RejectsInvalidOrUnboundedInput(string? expression)
        => AmountExpression.TryEvaluate(expression, out _).ShouldBeFalse();

    [Fact]
    public void ExcessivelyLongInputDoesNotProduceAValue()
    {
        AmountExpression.TryEvaluate(new string('1', 201), out var value).ShouldBeFalse();
        value.ShouldBe(0);
    }
}
