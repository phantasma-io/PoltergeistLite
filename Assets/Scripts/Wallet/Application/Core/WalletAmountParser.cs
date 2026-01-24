using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

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

            var trimmed = input.Trim();
            if (decimals == 0)
            {
                // Whole-number tokens should use the current culture's grouping rules and reject decimal separators.
                return TryParseWholeNumber(trimmed, CultureInfo.CurrentCulture.NumberFormat, out value);
            }

            // For fractional tokens, keep the existing permissive parsing (strip spaces/underscores).
            trimmed = trimmed.Replace(" ", string.Empty).Replace("_", string.Empty);

            var lastDot = trimmed.LastIndexOf('.');
            var lastComma = trimmed.LastIndexOf(',');
            char? decimalSep = null;

            if (lastDot >= 0 && lastComma >= 0)
            {
                decimalSep = lastDot > lastComma ? '.' : ',';
            }
            else if (lastDot >= 0)
            {
                decimalSep = decimals > 0 ? '.' : (char?)null;
            }
            else if (lastComma >= 0)
            {
                if (decimals > 0)
                {
                    var commaCount = trimmed.Count(c => c == ',');
                    var digitsAfter = trimmed.Length - lastComma - 1;
                    decimalSep = (digitsAfter == 3 && commaCount >= 1) ? (char?)null : ',';
                }
                else
                {
                    decimalSep = null;
                }
            }

            var sb = new StringBuilder(trimmed.Length);

            for (int i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];

                if (c == '.' || c == ',')
                {
                    if (decimalSep.HasValue && c == decimalSep.Value && i == (decimalSep == '.' ? lastDot : lastComma))
                    {
                        sb.Append('.');
                    }

                    // treat all other separators as thousands separators; skip them
                    continue;
                }

                sb.Append(c);
            }

            var normalized = sb.ToString();
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

        private static bool TryParseWholeNumber(string input, NumberFormatInfo format, out BigInteger value)
        {
            value = BigInteger.Zero;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var numberFormat = format ?? CultureInfo.InvariantCulture.NumberFormat;
            var decimalSeparator = numberFormat.NumberDecimalSeparator ?? string.Empty;
            if (!string.IsNullOrEmpty(decimalSeparator) && input.Contains(decimalSeparator))
            {
                // Decimal separators are not allowed when token decimals are zero.
                return false;
            }

            // Allow thousands separators according to the current culture settings.
            return BigInteger.TryParse(input, NumberStyles.Integer | NumberStyles.AllowThousands, numberFormat, out value);
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
