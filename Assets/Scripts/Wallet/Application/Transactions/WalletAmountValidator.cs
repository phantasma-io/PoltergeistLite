using System;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Protocol;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Validates and parses user-entered amounts against token decimals and min/max rules.
    /// </summary>
    public sealed class WalletAmountValidator
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletAmountValidator(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletAmountValidationResult ParseAndValidate(string input, string symbol, decimal minAmount, decimal maxAmount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return WalletAmountValidationResult.Fail("Account manager is not available yet.");
            }

            uint decimals;
            try
            {
                decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            }
            catch (Exception e)
            {
                return WalletAmountValidationResult.Fail($"Cannot load token decimals for {symbol}. {e.Message}");
            }

            var amount = ParseNumber(input);
            if (accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation)
            {
                return WalletAmountValidationResult.Create(amount);
            }

            if (amount <= 0 || !ValidDecimals(amount, decimals))
            {
                return WalletAmountValidationResult.Fail("Invalid amount!");
            }

            if (amount > maxAmount)
            {
                return WalletAmountValidationResult.Fail($"Not enough {symbol}!");
            }

            if (amount < minAmount)
            {
                return WalletAmountValidationResult.Fail($"Amount is too small.\nMinimum accepted is {minAmount} {symbol}!");
            }

            return WalletAmountValidationResult.Create(amount);
        }

        private static bool ValidDecimals(decimal amount, uint decimals)
        {
            if (decimals > 0)
            {
                return true;
            }

            var temp = amount - (long)amount;
            return temp == 0;
        }

        private static decimal ParseNumber(string s)
        {
            s = s.Trim().Replace(" ", "").Replace("_", "");
            s = s.Replace(",", System.Globalization.CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator);
            decimal result;
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            return -1;
        }
    }

    public sealed class WalletAmountValidationResult
    {
        private WalletAmountValidationResult(bool success, decimal amount, string error)
        {
            Success = success;
            Amount = amount;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public decimal Amount { get; }
        public string Error { get; }

        public static WalletAmountValidationResult Create(decimal amount)
        {
            return new WalletAmountValidationResult(true, amount, null);
        }

        public static WalletAmountValidationResult Fail(string error)
        {
            return new WalletAmountValidationResult(false, 0, error);
        }
    }
}
