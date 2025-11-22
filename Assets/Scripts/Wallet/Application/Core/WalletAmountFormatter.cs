using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Formats monetary values in a consistent way across UI layers.
    /// </summary>
    public static class WalletAmountFormatter
    {
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
    }

    public enum MoneyFormatType
    {
        Short,
        Standard,
        Long
    }
}
