using System.Globalization;

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
            var parser = new Parser(expression.Replace(",", string.Empty, StringComparison.Ordinal)
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
