using System.Globalization;
using System.Text;

namespace YHAB.SharedKernel.Budgeting;

/// <summary>Evaluates bounded decimal arithmetic without executing code.</summary>
public static class AmountExpression
{
    public const decimal MaximumAmount = 999_999_999.99m;

    public static bool TryEvaluate(string? expression, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 200)
        {
            return false;
        }

        try
        {
            var parser = new Parser(NormalizeDecimalSeparators(expression)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace("$", string.Empty, StringComparison.Ordinal));
            var value = parser.Parse();
            if (Math.Abs(value) > MaximumAmount)
            {
                return false;
            }

            amount = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or DivideByZeroException)
        {
            return false;
        }
    }

    /// <summary>
    /// A device keyboard offers the local decimal separator, which can be a comma.
    /// Reading every comma as thousands grouping turned a typed <c>12,50</c> into
    /// <c>1250.00</c>, so a dotted fraction otherwise stays the only decimal form.
    /// </summary>
    private static string NormalizeDecimalSeparators(string expression)
    {
        var builder = new StringBuilder(expression.Length);
        var index = 0;
        while (index < expression.Length)
        {
            if (!IsNumberCharacter(expression[index]))
            {
                builder.Append(expression[index++]);
                continue;
            }

            var start = index;
            while (index < expression.Length && IsNumberCharacter(expression[index]))
            {
                index++;
            }

            builder.Append(NormalizeNumber(expression.AsSpan(start, index - start)));
        }

        return builder.ToString();
    }

    private static bool IsNumberCharacter(char character) => char.IsAsciiDigit(character) || character is ',' or '.';

    private static string NormalizeNumber(ReadOnlySpan<char> number)
    {
        var comma = number.LastIndexOf(',');
        var fraction = number.Length - comma - 1;
        if (comma < 0 || fraction is < 1 or > 2)
        {
            return number.ToString();
        }

        // A one- or two-digit tail after the final comma is a fraction, so any
        // remaining separators in the whole part were grouping under either
        // convention ("1,234.56" and "1.234,56" both resolve to 1234.56 here).
        var whole = new StringBuilder(number.Length);
        foreach (var character in number[..comma])
        {
            if (char.IsAsciiDigit(character))
            {
                whole.Append(character);
            }
        }

        return string.Concat(whole.Length == 0 ? "0" : whole.ToString(), ".", number[(comma + 1)..].ToString());
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public decimal Parse()
        {
            var result = Sum();
            Whitespace();
            if (_position != text.Length)
            {
                throw new FormatException("Unexpected expression text.");
            }

            return result;
        }

        private decimal Sum()
        {
            var value = Product();
            while (true)
            {
                if (Take('+'))
                {
                    value += Product();
                }
                else if (Take('-'))
                {
                    value -= Product();
                }
                else
                {
                    return value;
                }
            }
        }

        private decimal Product()
        {
            var value = Factor();
            while (true)
            {
                if (Take('*'))
                {
                    value *= Factor();
                }
                else if (Take('/'))
                {
                    value /= Factor();
                }
                else
                {
                    return value;
                }
            }
        }

        private decimal Factor()
        {
            if (Take('+'))
            {
                return Factor();
            }

            if (Take('-'))
            {
                return -Factor();
            }

            if (Take('('))
            {
                var result = Sum();
                if (!Take(')'))
                {
                    throw new FormatException("Missing closing parenthesis.");
                }

                return result;
            }

            Whitespace();
            var start = _position;
            while (_position < text.Length && (char.IsAsciiDigit(text[_position]) || text[_position] == '.'))
            {
                _position++;
            }

            if (!decimal.TryParse(text.AsSpan(start, _position - start), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException("Enter a number.");
            }

            return value;
        }

        private bool Take(char token)
        {
            Whitespace();
            if (_position >= text.Length || text[_position] != token)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void Whitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }
    }
}
