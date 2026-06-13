using System;
using System.Numerics;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds fungible transfer drafts so UI layers stay thin.
    /// </summary>
    public sealed class WalletTransferService
    {
        private readonly Func<AccountManager> _accountProvider;
        private readonly WalletFeeRequirement _feeRequirement;

        public WalletTransferService(Func<AccountManager> accountProvider, WalletFeeRequirement feeRequirement)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _feeRequirement = feeRequirement ?? throw new ArgumentNullException(nameof(feeRequirement));
        }

        public ValidationResult<BigInteger, BigInteger> GetFungibleAvailability(string symbol)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<BigInteger, BigInteger>.Fail("Account manager is not available yet.");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return ValidationResult<BigInteger, BigInteger>.Fail("Account state is unavailable.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult<BigInteger, BigInteger>.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            var minAmount = ComputeMinAmount(decimals, accountManager.Settings.balanceDisplayPrecision);
            var available = state.GetAvailableAmount(symbol);
            if (available < minAmount && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
            {
                return ValidationResult<BigInteger, BigInteger>.Fail($"Not enough {symbol}.");
            }

            return ValidationResult<BigInteger, BigInteger>.Ok(minAmount, available);
        }

        public WalletTransactionDraftResult BuildFungibleTransferDraft(string symbol, BigInteger requestedAmount, string destinationText)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return WalletTransactionDraftResult.Fail("Account manager is not available yet.");
            }

            if (!TryValidateTransferRequest(accountManager, symbol, requestedAmount, destinationText, out var source, out var destination, out var amount, out var decimals, out var bigIntAmount, out var availableBalance, out var validationError))
            {
                return WalletTransactionDraftResult.Fail(validationError);
            }

            var feeDecimals = Tokens.GetTokenDecimals(DomainSettings.FuelTokenSymbol, accountManager.CurrentPlatform);
            var minFee = WalletAmountParser.FromDecimal(0.1m, feeDecimals);
            if (!EnsureKcal(accountManager, minFee, out var feeError))
            {
                return WalletTransactionDraftResult.Fail(feeError);
            }

            var chain = DomainSettings.RootChainName;
            var gasPrice = accountManager.Settings.feePrice;
            var gasLimit = accountManager.Settings.feeLimit;

            if (!accountManager.Settings.useVmTransactions)
            {
                if (bigIntAmount < 0 || bigIntAmount > ulong.MaxValue)
                {
                    Log.WriteWarning($"Scriptless transfer blocked for {symbol}: amount {bigIntAmount} exceeds UInt64 range.");
                    return WalletTransactionDraftResult.Fail("Scriptless transactions can't transfer this amount. Enable VM transactions in Developer settings and try again.");
                }

                try
                {
                    var tokenCarbonId = Tokens.GetTokenCarbonId(symbol, accountManager.CurrentPlatform);

                    var tx = new TxMsg
                    {
                        type = TxTypes.TransferFungible,
                        expiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(),
                        maxGas = (ulong)accountManager.Settings.scriptlessMaxGas,
                        maxData = (ulong)accountManager.Settings.scriptlessMaxData,
                        gasFrom = new Bytes32(source.GetPublicKey()),
                        payload = new SmallString(accountManager.WalletIdentifier),
                        msg = new TxMsgTransferFungible
                        {
                            to = new Bytes32(destination.GetPublicKey()),
                            tokenId = tokenCarbonId,
                            amount = (ulong)bigIntAmount
                        }
                    };

                    var description = BuildDescription(symbol, amount, decimals, destination);
                    var plan = WalletTransactionDraft.ForCarbon(description, tx, chain, gasPrice, gasLimit);
                    return WalletTransactionDraftResult.CreateSuccess(plan, bigIntAmount);
                }
                catch (Exception e)
                {
                    return WalletTransactionDraftResult.Fail($"Something went wrong while building transaction.\n{e.Message}");
                }
            }

            try
            {
                var sb = new ScriptBuilder();
                sb.AllowGas(source, Address.Null, gasPrice, gasLimit);

                if (symbol == "KCAL" && amount == availableBalance)
                {
                    sb.TransferBalance(symbol, source, destination);
                }
                else
                {
                    sb.TransferTokens(symbol, source, destination, bigIntAmount);
                }

                sb.SpendGas(source);
                var script = sb.EndScript();

                var description = BuildDescription(symbol, amount, decimals, destination);
                var plan = WalletTransactionDraft.ForSingleScript(description, script, chain, gasPrice, gasLimit, ProofOfWork.None);
                return WalletTransactionDraftResult.CreateSuccess(plan, bigIntAmount);
            }
            catch (Exception e)
            {
                return WalletTransactionDraftResult.Fail($"Something went wrong while building transaction.\n{e.Message}");
            }
        }

        private static string BuildDescription(string symbol, BigInteger amount, uint decimals, Address destination)
        {
            var precision = AccountManager.Instance?.Settings?.balanceDisplayPrecision ?? 4;
            return $"Transfer {WalletAmountFormatter.Format(amount, decimals, precision)} {symbol}\nDestination: {WalletTextFormatter.AbbreviateMiddleForDisplay(destination.Text, 6, 6)}";
        }

        private bool TryValidateTransferRequest(AccountManager accountManager, string symbol, BigInteger requestedAmount, string destinationText, out Address source, out Address destination, out BigInteger amount, out uint decimals, out BigInteger bigIntAmount, out BigInteger availableBalance, out string error)
        {
            source = Address.Null;
            destination = Address.Null;
            amount = 0;
            decimals = 0;
            bigIntAmount = 0;
            availableBalance = 0;
            error = null;

            var state = accountManager.CurrentState;
            if (state == null)
            {
                error = "Account state is unavailable.";
                return false;
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                error = $"Current platform must be {PlatformKind.Phantasma}";
                return false;
            }

            if (requestedAmount <= 0)
            {
                error = "Amount must be greater than zero.";
                return false;
            }

            try
            {
                source = Address.Parse(state.address);
            }
            catch
            {
                error = "Invalid source address.";
                return false;
            }

            try
            {
                destination = Address.Parse(destinationText);
            }
            catch
            {
                error = "Invalid destination address.";
                return false;
            }

            if (source == destination)
            {
                error = "Source and destination address must be different.";
                return false;
            }

            availableBalance = state.GetAvailableAmount(symbol);
            amount = requestedAmount;

            if (!(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation) && amount > availableBalance)
            {
                amount = availableBalance;
            }

            if (amount <= 0 && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
            {
                error = $"Not enough {symbol}.";
                return false;
            }

            decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            bigIntAmount = amount;

            return true;
        }

        private static BigInteger ComputeMinAmount(uint decimals, int displayPrecision)
        {
            if (decimals == 0)
            {
                return BigInteger.One;
            }

            var clampedPrecision = Math.Max(0, Math.Min(displayPrecision, (int)decimals));
            var exponent = (int)decimals - clampedPrecision;
            return BigInteger.Pow(10, exponent);
        }

        private bool EnsureKcal(AccountManager accountManager, BigInteger minAmount, out string error)
        {
            var success = true;
            string localError = null;

            _feeRequirement.EnsureKcal(minAmount, (feeResult, err) =>
            {
                if (feeResult != PromptResult.Success)
                {
                    success = false;
                    localError = string.IsNullOrEmpty(err) ? "KCAL is required to make transactions!" : err;
                }
            });

            error = localError;
            return success;
        }
    }
}
