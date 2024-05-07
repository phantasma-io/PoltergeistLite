using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Phantasma.Core.Numerics
{
    public static class UnitConversion
    {
        private static readonly string s_NumberDecimalSeparator = ".";
        private static readonly NumberFormatInfo s_NumberFormatInfo = new() { NumberDecimalSeparator = s_NumberDecimalSeparator };

        // TODO why not just BigInteger.Pow(10, units)???
        private static BigInteger GetMultiplier(int units)
        {
            BigInteger unitMultiplier = 1;
            while (units > 0)
            {
                unitMultiplier *= 10;
                units--;
            }

            return unitMultiplier;
        }

        public static string ToDecimalString(string amount, int tokenDecimals)
        {
            if (amount == "0" || tokenDecimals == 0)
                return amount;

            if (amount.Length <= tokenDecimals)
            {
                return "0" + s_NumberDecimalSeparator + amount.PadLeft(tokenDecimals, '0').TrimEnd('0');
            }

            var decimalPart = amount.Substring(amount.Length - tokenDecimals);
            decimalPart = decimalPart.Any(x => x != '0') ? decimalPart.TrimEnd('0') : null;
            return amount.Substring(0, amount.Length - tokenDecimals) + (decimalPart != null ? s_NumberDecimalSeparator + decimalPart : "");
        }

        public static decimal ToDecimal(string amount, int tokenDecimals)
        {
            return decimal.Parse(ToDecimalString(amount, tokenDecimals), s_NumberFormatInfo);
        }

        public static decimal ToDecimal(BigInteger value, int tokenDecimals)
        {
            return ToDecimal(value.ToString(), tokenDecimals);
        }

        public static BigInteger ToBigInteger(decimal n, int units)
        {
            var multiplier = GetMultiplier(units);
            var A = new BigInteger((long)n);
            var B = new BigInteger((long)multiplier);

            var fracPart = n - Math.Truncate(n);
            BigInteger C = 0;

            if (fracPart > 0)
            {
                var l = fracPart * (long)multiplier;
                C = new BigInteger((long)l);
            }

            return A * B + C;
        }

        public static BigInteger ConvertDecimals(BigInteger value, int decimalFrom, int decimalTo)
        {
            if (decimalFrom == decimalTo)
            {
                return value;
            }

            //doing "value * BigInteger.Pow(10, decimalTo - decimalFrom)" would not work for negative exponents as it would always be 0;
            //separating the calculations in two steps leads to only returning 0 when the final value would be < 1
            var fromFactor = BigInteger.Pow(10, decimalFrom);
            var toFactor = BigInteger.Pow(10, decimalTo);

            var output = value * toFactor / fromFactor;

            return output;
        }

        public static BigInteger GetUnitValue(int decimals)
        {
            return ToBigInteger(1, decimals);
        }
    }
}
