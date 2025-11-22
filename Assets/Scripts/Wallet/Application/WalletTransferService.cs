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
    /// Builds fungible transfer plans so UI layers stay thin.
    /// </summary>
    public sealed class WalletTransferService
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletTransferService(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletTransferPlanResult BuildFungibleTransferPlan(string symbol, decimal requestedAmount, string destinationText)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return WalletTransferPlanResult.Fail("Account manager is not available yet.");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return WalletTransferPlanResult.Fail("Account state is unavailable.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return WalletTransferPlanResult.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            if (requestedAmount <= 0)
            {
                return WalletTransferPlanResult.Fail("Amount must be greater than zero.");
            }

            Address source;
            Address destination;
            try
            {
                source = Address.Parse(state.address);
            }
            catch
            {
                return WalletTransferPlanResult.Fail("Invalid source address.");
            }

            try
            {
                destination = Address.Parse(destinationText);
            }
            catch
            {
                return WalletTransferPlanResult.Fail("Invalid destination address.");
            }

            if (source == destination)
            {
                return WalletTransferPlanResult.Fail("Source and destination address must be different.");
            }

            var balance = state.GetAvailableAmount(symbol);
            var amount = requestedAmount;

            if (amount > balance && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
            {
                amount = balance;
            }

            if (amount <= 0)
            {
                return WalletTransferPlanResult.Fail($"Not enough {symbol}.");
            }

            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            if (!ValidateDecimals(amount, decimals))
            {
                return WalletTransferPlanResult.Fail($"Invalid {symbol} amount.");
            }

            BigInteger bigIntAmount;
            try
            {
                bigIntAmount = UnitConversion.ToBigInteger(amount, decimals);
            }
            catch (Exception e)
            {
                return WalletTransferPlanResult.Fail($"Failed to convert amount: {e.Message}");
            }

            var chain = DomainSettings.RootChainName;
            var gasPrice = accountManager.Settings.feePrice;
            var gasLimit = accountManager.Settings.feeLimit;

            if (accountManager.Settings.preferScriptlessTxes)
            {
                if (bigIntAmount < 0 || bigIntAmount > ulong.MaxValue)
                {
                    Log.WriteWarning($"Scriptless transfer blocked for {symbol}: amount {bigIntAmount} exceeds UInt64 range.");
                    return WalletTransferPlanResult.Fail("Scriptless transactions currently can't transfer this amount. Please switch to Standard transactions in Settings and try again.");
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

                    var description = BuildDescription(symbol, amount, destination);
                    var plan = WalletTransferPlan.ForCarbon(description, tx, chain, gasPrice, gasLimit);
                    return WalletTransferPlanResult.CreateSuccess(plan, amount);
                }
                catch (Exception e)
                {
                    return WalletTransferPlanResult.Fail($"Something went wrong while building transaction.\n{e.Message}");
                }
            }
            else
            {
                try
                {
                    var sb = new ScriptBuilder();
                    sb.AllowGas(source, Address.Null, gasPrice, gasLimit);

                    if (symbol == "KCAL" && amount == balance)
                    {
                        sb.TransferBalance(symbol, source, destination);
                    }
                    else
                    {
                        sb.TransferTokens(symbol, source, destination, bigIntAmount);
                    }

                    sb.SpendGas(source);
                    var script = sb.EndScript();

                    var description = BuildDescription(symbol, amount, destination);
                    var plan = WalletTransferPlan.ForScript(description, script, chain, gasPrice, gasLimit, ProofOfWork.None);
                    return WalletTransferPlanResult.CreateSuccess(plan, amount);
                }
                catch (Exception e)
                {
                    return WalletTransferPlanResult.Fail($"Something went wrong while building transaction.\n{e.Message}");
                }
            }
        }

        private static bool ValidateDecimals(decimal amount, uint decimals)
        {
            if (decimals > 0)
            {
                return true;
            }

            var temp = amount - (long)amount;
            return temp == 0;
        }

        private static string BuildDescription(string symbol, decimal amount, Address destination)
        {
            return $"Transfer {FormatAmount(amount)} {symbol}\nDestination: {destination}";
        }

        private static string FormatAmount(decimal amount)
        {
            amount -= amount % 0.000000000001M;
            return amount.ToString("#,0.############");
        }
    }

    public sealed class WalletTransferPlanResult
    {
        private WalletTransferPlanResult(bool success, WalletTransferPlan plan, string error, decimal amount)
        {
            Success = success;
            Plan = plan;
            Error = error ?? string.Empty;
            Amount = amount;
        }

        public bool Success { get; }
        public WalletTransferPlan Plan { get; }
        public string Error { get; }
        public decimal Amount { get; }

        public static WalletTransferPlanResult CreateSuccess(WalletTransferPlan plan, decimal amount)
        {
            return new WalletTransferPlanResult(true, plan, null, amount);
        }

        public static WalletTransferPlanResult Fail(string error)
        {
            return new WalletTransferPlanResult(false, null, error, 0);
        }
    }

    public sealed class WalletTransferPlan
    {
        private WalletTransferPlan(string description, string chain, BigInteger gasPrice, BigInteger gasLimit, ProofOfWork pow, byte[] script, TxMsg? carbonTx, bool isCarbon)
        {
            Description = description ?? string.Empty;
            Chain = chain ?? string.Empty;
            GasPrice = gasPrice;
            GasLimit = gasLimit;
            PoW = pow;
            Script = script;
            CarbonTx = carbonTx;
            IsCarbonTransaction = isCarbon;
        }

        public string Description { get; }
        public string Chain { get; }
        public BigInteger GasPrice { get; }
        public BigInteger GasLimit { get; }
        public ProofOfWork PoW { get; }
        public byte[] Script { get; }
        public TxMsg? CarbonTx { get; }
        public bool IsCarbonTransaction { get; }

        public static WalletTransferPlan ForScript(string description, byte[] script, string chain, BigInteger gasPrice, BigInteger gasLimit, ProofOfWork pow)
        {
            return new WalletTransferPlan(description, chain, gasPrice, gasLimit, pow, script, null, false);
        }

        public static WalletTransferPlan ForCarbon(string description, TxMsg tx, string chain, BigInteger gasPrice, BigInteger gasLimit)
        {
            return new WalletTransferPlan(description, chain, gasPrice, gasLimit, ProofOfWork.None, null, tx, true);
        }
    }
}
