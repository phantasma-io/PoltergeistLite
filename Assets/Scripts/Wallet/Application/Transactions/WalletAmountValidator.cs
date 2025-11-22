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

        public ValidationResult<decimal> ParseAndValidate(string input, string symbol, decimal minAmount, decimal maxAmount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<decimal>.Fail("Account manager is not available yet.");
            }

            uint decimals;
            try
            {
                decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            }
            catch (Exception e)
            {
                return ValidationResult<decimal>.Fail($"Cannot load token decimals for {symbol}. {e.Message}");
            }

            var amount = ParseNumber(input);
            if (accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation)
            {
                return ValidationResult<decimal>.Ok(amount);
            }

            if (amount <= 0 || !ValidDecimals(amount, decimals))
            {
                return ValidationResult<decimal>.Fail("Invalid amount!");
            }

            if (amount > maxAmount)
            {
                return ValidationResult<decimal>.Fail($"Not enough {symbol}!");
            }

            if (amount < minAmount)
            {
                return ValidationResult<decimal>.Fail($"Amount is too small.\nMinimum accepted is {minAmount} {symbol}!");
            }

            return ValidationResult<decimal>.Ok(amount);
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
}
