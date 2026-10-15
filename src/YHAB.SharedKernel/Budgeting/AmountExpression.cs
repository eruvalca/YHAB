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

        if (!TryNormalizeDecimalSeparators(expression, out var normalized))
        {
            return false;
        }

        try
        {
            var parser = new Parser(normalized.Replace(",", string.Empty, StringComparison.Ordinal)
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
    /// <c>1250.00</c>, so a final one- or two-digit comma group becomes a fraction.
    /// A number with that shape but a malformed whole part, such as
    /// <c>1..234,56</c> or <c>1.2345,67</c>, stays invalid instead of being
    /// smoothed into a value.
    /// </summary>
    private static bool TryNormalizeDecimalSeparators(string expression, out string normalized)
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

            if (!TryNormalizeNumber(expression.AsSpan(start, index - start), out var number))
            {
                normalized = string.Empty;
                return false;
            }

            builder.Append(number);
        }

        normalized = builder.ToString();
        return true;
    }

    private static bool IsNumberCharacter(char character) => char.IsAsciiDigit(character) || character is ',' or '.';

    private static bool TryNormalizeNumber(ReadOnlySpan<char> number, out string normalized)
    {
        var comma = number.LastIndexOf(',');
        var fractionLength = number.Length - comma - 1;
        if (comma < 0 || fractionLength is < 1 or > 2)
        {
            normalized = number.ToString();
            return true;
        }

        var fraction = number[(comma + 1)..];
        if (!IsAllDigits(fraction) || !IsGroupedWhole(number[..comma]))
        {
            normalized = string.Empty;
            return false;
        }

        var whole = new StringBuilder(number.Length);
        foreach (var character in number[..comma])
        {
            if (char.IsAsciiDigit(character))
            {
                whole.Append(character);
            }
        }

        normalized = string.Concat(whole.Length == 0 ? "0" : whole.ToString(), ".", fraction.ToString());
        return true;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A plain integer, or thousands groups of exactly three after one to three leading digits.</summary>
    private static bool IsGroupedWhole(ReadOnlySpan<char> whole)
    {
        if (whole.Length == 0)
        {
            return true;
        }

        if (whole.IndexOf('.') < 0)
        {
            return IsAllDigits(whole);
        }

        var length = 0;
        var group = 0;
        foreach (var character in whole)
        {
            if (character == '.')
            {
                if (group == 0 ? length is < 1 or > 3 : length != 3)
                {
                    return false;
                }

                group++;
                length = 0;
                continue;
            }

            if (!char.IsAsciiDigit(character))
            {
                return false;
            }

            length++;
        }

        return length == 3;
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
