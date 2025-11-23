using System;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Protocol;
using Poltergeist;
using System.Numerics;

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

        public ValidationResult<BigInteger> ParseAndValidate(string input, string symbol, BigInteger minAmount, BigInteger maxAmount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<BigInteger>.Fail("Account manager is not available yet.");
            }

            uint decimals;
            try
            {
                decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            }
            catch (Exception e)
            {
                return ValidationResult<BigInteger>.Fail($"Cannot load token decimals for {symbol}. {e.Message}");
            }

            if (!WalletAmountParser.TryParse(input, decimals, out var amount))
            {
                return ValidationResult<BigInteger>.Fail($"Invalid {symbol} amount.");
            }

            if (accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation)
            {
                return ValidationResult<BigInteger>.Ok(amount);
            }

            if (amount <= 0)
            {
                return ValidationResult<BigInteger>.Fail("Invalid amount!");
            }

            if (amount > maxAmount)
            {
                return ValidationResult<BigInteger>.Fail($"Not enough {symbol}!");
            }

            if (amount < minAmount)
            {
                return ValidationResult<BigInteger>.Fail($"Amount is too small.\nMinimum accepted is {WalletAmountFormatter.Format(minAmount, decimals)} {symbol}!");
            }

            return ValidationResult<BigInteger>.Ok(amount);
        }

    }
}
