using System;
using System.Globalization;
using System.Numerics;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Parses user-entered/token amounts into raw BigInteger values respecting token decimals.
    /// </summary>
    public static class WalletAmountParser
    {
        public static bool TryParse(string input, uint decimals, out BigInteger value)
        {
            value = BigInteger.Zero;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var normalized = input.Trim();

            var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var allowComma = decimalSeparator == ",";

            if (normalized.Contains(".") && normalized.Contains(","))
            {
                return false; // mixed separators not allowed
            }

            if (!allowComma && normalized.Contains(","))
            {
                return false; // comma not accepted in this locale
            }

            if (allowComma)
            {
                normalized = normalized.Replace(',', '.');
            }

            var parts = normalized.Split('.');
            if (parts.Length > 2)
            {
                return false;
            }

            var intPart = parts[0];
            if (string.IsNullOrEmpty(intPart))
            {
                intPart = "0";
            }

            var fracPart = parts.Length == 2 ? parts[1] : string.Empty;
            if (decimals == 0 && fracPart.Length > 0)
            {
                return false;
            }

            if (fracPart.Length > decimals)
            {
                return false;
            }

            var combined = intPart + fracPart.PadRight((int)decimals, '0');
            if (!BigInteger.TryParse(combined, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return true;
        }

        public static BigInteger Parse(string input, uint decimals)
        {
            if (TryParse(input, decimals, out var value))
            {
                return value;
            }

            throw new FormatException($"Cannot parse amount '{input}' with {decimals} decimals.");
        }

        public static BigInteger FromDecimal(decimal amount, uint decimals)
        {
            if (decimals == 0)
            {
                var truncated = decimal.Truncate(amount);
                if (truncated != amount)
                {
                    throw new FormatException($"Cannot parse amount '{amount}' with 0 decimals.");
                }

                return new BigInteger(truncated);
            }

            return Parse(amount.ToString(CultureInfo.InvariantCulture), decimals);
        }
    }
}
