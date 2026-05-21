using System;
using System.Globalization;
using System.Numerics;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Formats monetary values in a consistent way across UI layers.
    /// </summary>
    public static class WalletAmountFormatter
    {
        public static bool TryToDecimal(BigInteger raw, uint decimals, out decimal value)
        {
            var scale = BigInteger.Pow(10, (int)decimals);
            var quotient = BigInteger.DivRem(raw, scale, out var remainder);

            var max = (BigInteger)decimal.MaxValue;
            var min = (BigInteger)decimal.MinValue;

            if (quotient > max || quotient < min)
            {
                value = 0;
                return false;
            }

            if ((quotient == max || quotient == min) && remainder != 0)
            {
                value = 0;
                return false;
            }

            value = (decimal)quotient;
            if (remainder != 0)
            {
                try
                {
                    // High-decimal tokens can have a scale (10^decimals) that does not fit in decimal even when the
                    // final human-readable amount itself would. Guard the intermediate conversion and let callers
                    // decide whether they want to fall back to an approximate display-only path.
                    var fraction = (decimal)remainder / (decimal)scale;
                    decimal candidate;
                    candidate = value + fraction;

                    if (candidate == decimal.MaxValue || candidate == decimal.MinValue)
                    {
                        value = 0;
                        return false;
                    }

                    value = candidate;
                }
                catch (OverflowException)
                {
                    value = 0;
                    return false;
                }
            }

            return true;
        }

        public static bool TryToApproxDecimal(BigInteger raw, uint decimals, int precision, out decimal value)
        {
            // For fiat estimates we only need a stable approximation, not the full token precision. Reuse the
            // string formatter, which already handles arbitrarily large BigInteger values without going through
            // a decimal scale such as 10^64.
            var formatted = Format(raw, decimals, precision);
            return decimal.TryParse(formatted, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        public static string Format(BigInteger raw, uint decimals, MoneyFormatType formatType = MoneyFormatType.Standard)
        {
            var precision = formatType switch
            {
                MoneyFormatType.Short => 2,
                MoneyFormatType.Long => 12,
                _ => 4
            };
            return Format(raw, decimals, precision);
        }

        public static string Format(BigInteger raw, uint decimals, int precision)
        {
            if (precision < 0)
            {
                precision = 0;
            }
            if (precision > 18)
            {
                precision = 18;
            }

            var negative = raw < 0;
            var abs = BigInteger.Abs(raw);
            var digits = abs.ToString();

            string intPartDigits;
            string fracPartDigits;

            if (decimals == 0)
            {
                intPartDigits = digits;
                fracPartDigits = string.Empty;
            }
            else if (digits.Length <= decimals)
            {
                intPartDigits = "0";
                var padded = digits.PadLeft((int)decimals, '0');
                fracPartDigits = padded;
            }
            else
            {
                intPartDigits = digits.Substring(0, digits.Length - (int)decimals);
                fracPartDigits = digits.Substring(digits.Length - (int)decimals);
            }

            if (precision >= 0 && fracPartDigits.Length > precision)
            {
                fracPartDigits = fracPartDigits.Substring(0, precision);
            }

            fracPartDigits = TrimTrailingZeros(fracPartDigits);
            var intPartFormatted = InsertThousandsSeparators(intPartDigits);

            var sign = negative ? "-" : string.Empty;
            if (string.IsNullOrEmpty(fracPartDigits))
            {
                return $"{sign}{intPartFormatted}";
            }

            return $"{sign}{intPartFormatted}.{fracPartDigits}";
        }

        public static string Format(decimal amount, MoneyFormatType formatType = MoneyFormatType.Standard)
        {
            switch (formatType)
            {
                case MoneyFormatType.Short:
                    amount -= amount % 0.01m; // Getting rid of deceiving rounding.
                    return amount.ToString("#,0.##");
                case MoneyFormatType.Standard:
                    amount -= amount % 0.0001m;
                    return amount.ToString("#,0.####");
                case MoneyFormatType.Long:
                    amount -= amount % 0.000000000001m;
                    return amount.ToString("#,0.############");
                default:
                    return amount.ToString();
            }
        }

        private static string InsertThousandsSeparators(string digits)
        {
            if (string.IsNullOrEmpty(digits))
            {
                return "0";
            }

            var result = string.Empty;
            var count = 0;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                result = digits[i] + result;
                count++;
                if (count == 3 && i != 0)
                {
                    result = CultureInfo.InvariantCulture.NumberFormat.NumberGroupSeparator + result;
                    count = 0;
                }
            }
            return result;
        }

        private static string TrimTrailingZeros(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            int i = value.Length;
            while (i > 0 && value[i - 1] == '0')
            {
                i--;
            }

            return value.Substring(0, i);
        }
    }

    public enum MoneyFormatType
    {
        Short,
        Standard,
        Long
    }
}
