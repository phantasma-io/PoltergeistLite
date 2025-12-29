using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds burn transaction drafts for fungible tokens and NFTs.
    /// </summary>
    public sealed class WalletBurnService
    {
        private readonly Func<AccountManager> _accountProvider;
        private readonly WalletNftTransactionBuilder _nftBuilder;
        private readonly WalletFeeRequirement _feeRequirement;

        public WalletBurnService(Func<AccountManager> accountProvider, WalletNftTransactionBuilder nftBuilder, WalletFeeRequirement feeRequirement)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _nftBuilder = nftBuilder ?? throw new ArgumentNullException(nameof(nftBuilder));
            _feeRequirement = feeRequirement ?? throw new ArgumentNullException(nameof(feeRequirement));
        }

        public ValidationResult<WalletTransactionDraft> PrepareFungibleBurn(string symbol, BigInteger availableAmount, BigInteger requestedAmount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult<WalletTransactionDraft>.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account state is unavailable.");
            }

            if (requestedAmount <= 0 || requestedAmount > availableAmount)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Invalid burn amount.");
            }

            var feeDecimals = Tokens.GetTokenDecimals(DomainSettings.FuelTokenSymbol, accountManager.CurrentPlatform);
            var minFee = WalletAmountParser.FromDecimal(0.1m, feeDecimals);
            var feeCheck = EnsureKcal(accountManager, minFee);
            if (!feeCheck.Success)
            {
                return ValidationResult<WalletTransactionDraft>.Fail(feeCheck.Error);
            }

            var draftResult = BuildFungibleBurnDraft(symbol, requestedAmount, state, accountManager);
            if (!draftResult.Success)
            {
                return ValidationResult<WalletTransactionDraft>.Fail(draftResult.Error);
            }

            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            var message = $"Are you sure you want to burn {WalletAmountFormatter.Format(draftResult.Amount, decimals, accountManager.Settings.balanceDisplayPrecision)} {symbol} tokens?";
            return ValidationResult<WalletTransactionDraft>.Ok(draftResult.Draft, message);
        }

        public WalletTransactionDraftResult BuildFungibleBurnDraft(string symbol, BigInteger amount)
        {
            var accountManager = _accountProvider();
            var state = accountManager?.CurrentState;
            return BuildFungibleBurnDraft(symbol, amount, state, accountManager);
        }

        private WalletTransactionDraftResult BuildFungibleBurnDraft(string symbol, BigInteger amount, AccountState state, AccountManager accountManager)
        {
            if (accountManager == null)
            {
                return WalletTransactionDraftResult.Fail("Account manager is not available yet.");
            }

            if (state == null)
            {
                return WalletTransactionDraftResult.Fail("Account state is unavailable.");
            }

            var balance = state.balances?.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (balance == null)
            {
                return WalletTransactionDraftResult.Fail($"{symbol} balance is not available.");
            }

            var available = balance.Available;
            var burnAmount = amount;
            if (burnAmount > available && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
            {
                burnAmount = available;
            }

            if (burnAmount <= 0)
            {
                return WalletTransactionDraftResult.Fail($"Not enough {symbol} to burn.");
            }

            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            var target = Address.Parse(state.address);

            if (accountManager.Settings.preferScriptlessTxes)
            {
                return BuildScriptlessFungibleBurnDraft(symbol, burnAmount, target, accountManager, decimals);
            }

            var sb = new ScriptBuilder();
            sb.AllowGas(target, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallInterop("Runtime.BurnTokens", target, symbol, burnAmount);
            sb.SpendGas(target);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript($"Burn {WalletAmountFormatter.Format(burnAmount, decimals, accountManager.Settings.balanceDisplayPrecision)} {symbol} tokens", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, burnAmount);
        }

        private WalletTransactionDraftResult BuildScriptlessFungibleBurnDraft(string symbol, BigInteger burnAmount, Address source, AccountManager accountManager, uint decimals)
        {
            try
            {
                var tokenCarbonId = Tokens.GetTokenCarbonId(symbol, accountManager.CurrentPlatform);
                var tx = new TxMsg
                {
                    type = TxTypes.BurnFungible,
                    expiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(),
                    maxGas = (ulong)accountManager.Settings.scriptlessMaxGas,
                    maxData = (ulong)accountManager.Settings.scriptlessMaxData,
                    gasFrom = new Bytes32(source.GetPublicKey()),
                    payload = new SmallString(accountManager.WalletIdentifier),
                    msg = new TxMsgBurnFungible
                    {
                        tokenId = tokenCarbonId,
                        amount = new IntX(burnAmount)
                    }
                };

                var description = $"Burn {WalletAmountFormatter.Format(burnAmount, decimals, accountManager.Settings.balanceDisplayPrecision)} {symbol} tokens";
                var plan = WalletTransactionDraft.ForCarbon(description, tx, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                return WalletTransactionDraftResult.CreateSuccess(plan, burnAmount);
            }
            catch (Exception e)
            {
                return WalletTransactionDraftResult.Fail($"Something went wrong while building transaction.\n{e.Message}");
            }
        }

        public ValidationResult<WalletTransactionDraft> PrepareNftBurn(string symbol, IEnumerable<string> nftIds)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult<WalletTransactionDraft>.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account state is unavailable.");
            }

            var ids = nftIds?.Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
            if (ids.Count == 0)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("No NFTs selected.");
            }

            var feeDecimals = Tokens.GetTokenDecimals(DomainSettings.FuelTokenSymbol, accountManager.CurrentPlatform);
            var minFee = WalletAmountParser.FromDecimal(0.1m, feeDecimals);
            var feeCheck = EnsureKcal(accountManager, minFee);
            if (!feeCheck.Success)
            {
                return ValidationResult<WalletTransactionDraft>.Fail(feeCheck.Error);
            }

            var target = Address.Parse(state.address);
            var message = $"Are you sure you want to burn (destroy) {ids.Count} {symbol} NFTs?";

            if (accountManager.Settings.preferScriptlessTxes)
            {
                var scriptlessDraft = BuildScriptlessNftBurnDraft(symbol, ids, target, accountManager);
                if (!scriptlessDraft.Success)
                {
                    Log.WriteWarning($"[Burn] Scriptless NFT burn build failed. symbol={symbol}, count={ids.Count}, ids={string.Join(", ", ids.Take(3))}, error={scriptlessDraft.Error}");
                    return ValidationResult<WalletTransactionDraft>.Fail(scriptlessDraft.Error);
                }

                return ValidationResult<WalletTransactionDraft>.Ok(scriptlessDraft.Draft, message);
            }

            byte[] script;
            try
            {
                script = _nftBuilder.BuildBurnScript(symbol, ids, target);
            }
            catch (Exception e)
            {
                return ValidationResult<WalletTransactionDraft>.Fail($"Failed to build NFT burn transaction.\n{e.Message}");
            }

            var plan = WalletTransactionDraft.ForSingleScript($"Burn {ids.Count} {symbol} NFTs", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return ValidationResult<WalletTransactionDraft>.Ok(plan, message);
        }

        private WalletTransactionDraftResult BuildScriptlessNftBurnDraft(string symbol, IReadOnlyList<string> ids, Address source, AccountManager accountManager)
        {
            if (ids == null || ids.Count == 0)
            {
                return WalletTransactionDraftResult.Fail("No NFTs selected.");
            }

            try
            {
                var tokenCarbonId = Tokens.GetTokenCarbonId(symbol, accountManager.CurrentPlatform);
                var owner = new Bytes32(source.GetPublicKey());

                if (ids.Count == 1)
                {
                    var tokenData = accountManager.GetNft(symbol, ids[0]);
                    if (string.IsNullOrWhiteSpace(tokenData?.carbonNftAddress))
                    {
                        var errorMessage = MissingCarbonAddressError;
                        Log.WriteWarning($"[Burn] Scriptless NFT burn blocked for {symbol}: {errorMessage} id={ids[0]}, carbonNftAddress={tokenData?.carbonNftAddress ?? "null"}.");
                        return WalletTransactionDraftResult.Fail(errorMessage);
                    }

                    ulong nftTokenCarbonId;
                    ulong instanceId;
                    try
                    {
                        TokenHelperExtensions.UnpackNftAddress(tokenData.carbonNftAddress, out nftTokenCarbonId, out instanceId);
                    }
                    catch (Exception)
                    {
                        var errorMessage = InvalidCarbonAddressError;
                        Log.WriteWarning($"[Burn] Scriptless NFT burn blocked for {symbol}: {errorMessage} id={ids[0]}, carbonNftAddress={tokenData.carbonNftAddress}.");
                        return WalletTransactionDraftResult.Fail(errorMessage);
                    }

                    if (nftTokenCarbonId != tokenCarbonId)
                    {
                        Log.WriteWarning($"[Burn] Scriptless NFT burn token id mismatch for {symbol}: tokens list={tokenCarbonId}, nft data={nftTokenCarbonId}, id={ids[0]}. Using NFT data value.");
                        tokenCarbonId = nftTokenCarbonId;
                    }

                    var tx = new TxMsg
                    {
                        type = TxTypes.BurnNonFungible,
                        expiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(),
                        maxGas = (ulong)accountManager.Settings.scriptlessMaxGas,
                        maxData = (ulong)accountManager.Settings.scriptlessMaxData,
                        gasFrom = owner,
                        payload = new SmallString(accountManager.WalletIdentifier),
                        msg = new TxMsgBurnNonFungible
                        {
                            tokenId = tokenCarbonId,
                            instanceId = instanceId
                        }
                    };

                    var description = $"Burn {ids.Count} {symbol} NFTs";
                    var plan = WalletTransactionDraft.ForCarbon(description, tx, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                    return WalletTransactionDraftResult.CreateSuccess(plan);
                }

                var instanceIds = new ulong[ids.Count];
                ulong? selectedTokenCarbonId = null;
                for (var i = 0; i < ids.Count; i++)
                {
                    var tokenData = accountManager.GetNft(symbol, ids[i]);
                    if (string.IsNullOrWhiteSpace(tokenData?.carbonNftAddress))
                    {
                        var errorMessage = MissingCarbonAddressError;
                        Log.WriteWarning($"[Burn] Scriptless NFT burn blocked for {symbol}: {errorMessage} id={ids[i]} (index {i}), carbonNftAddress={tokenData?.carbonNftAddress ?? "null"}.");
                        return WalletTransactionDraftResult.Fail(errorMessage);
                    }

                    ulong nftTokenCarbonId;
                    ulong instanceId;
                    try
                    {
                        TokenHelperExtensions.UnpackNftAddress(tokenData.carbonNftAddress, out nftTokenCarbonId, out instanceId);
                    }
                    catch (Exception)
                    {
                        var errorMessage = InvalidCarbonAddressError;
                        Log.WriteWarning($"[Burn] Scriptless NFT burn blocked for {symbol}: {errorMessage} id={ids[i]} (index {i}), carbonNftAddress={tokenData.carbonNftAddress}.");
                        return WalletTransactionDraftResult.Fail(errorMessage);
                    }

                    if (selectedTokenCarbonId == null)
                    {
                        selectedTokenCarbonId = nftTokenCarbonId;
                    }
                    else if (selectedTokenCarbonId.Value != nftTokenCarbonId)
                    {
                        Log.WriteWarning($"[Burn] Scriptless NFT burn blocked for {symbol}: multiple carbon token ids selected ({selectedTokenCarbonId.Value} vs {nftTokenCarbonId}) at index {i}.");
                        return WalletTransactionDraftResult.Fail("Selected NFTs belong to different Carbon token ids. Please burn them separately.");
                    }

                    instanceIds[i] = instanceId;
                }

                if (selectedTokenCarbonId.HasValue && selectedTokenCarbonId.Value != tokenCarbonId)
                {
                    Log.WriteWarning($"[Burn] Scriptless NFT burn token id mismatch for {symbol}: tokens list={tokenCarbonId}, nft data={selectedTokenCarbonId.Value}. Using NFT data value.");
                    tokenCarbonId = selectedTokenCarbonId.Value;
                }

                var calls = new TxMsgCall[instanceIds.Length];
                for (var i = 0; i < instanceIds.Length; i++)
                {
                    calls[i] = new TxMsgCall
                    {
                        moduleId = (uint)ModuleId.Token,
                        methodId = (uint)TokenContract_Methods.BurnNonFungible,
                        args = BuildBurnNonFungibleCallArgs(tokenCarbonId, owner, new[] { instanceIds[i] })
                    };
                }

                var multiTx = new TxMsg
                {
                    type = TxTypes.Call_Multi,
                    expiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(),
                    maxGas = (ulong)accountManager.Settings.scriptlessMaxGas,
                    maxData = (ulong)accountManager.Settings.scriptlessMaxData,
                    gasFrom = owner,
                    payload = new SmallString(accountManager.WalletIdentifier),
                    msg = new TxMsgCall_Multi
                    {
                        calls = calls
                    }
                };

                var multiDescription = $"Burn {ids.Count} {symbol} NFTs";
                var multiPlan = WalletTransactionDraft.ForCarbon(multiDescription, multiTx, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                return WalletTransactionDraftResult.CreateSuccess(multiPlan);
            }
            catch (Exception e)
            {
                Log.WriteWarning($"[Burn] Scriptless NFT burn build threw an exception for {symbol} (count={ids?.Count ?? 0}): {e}");
                return WalletTransactionDraftResult.Fail($"Something went wrong while building transaction.\n{e.Message}");
            }
        }

        private static byte[] BuildBurnNonFungibleCallArgs(ulong tokenCarbonId, Bytes32 owner, ulong[] instanceIds)
        {
            using var argsStream = new MemoryStream();
            using var writer = new BinaryWriter(argsStream);
            writer.Write8(tokenCarbonId);
            writer.Write32(owner);
            writer.WriteArray64(instanceIds);
            return argsStream.ToArray();
        }

        private const string MissingCarbonAddressError = "NFT data is missing the Carbon address. Refresh the NFT list and try again.";
        private const string InvalidCarbonAddressError = "NFT Carbon address is invalid. Refresh the NFT list and try again.";


        public ValidationResult EnsureKcal(AccountManager accountManager, BigInteger minAmount)
        {
            var result = ValidationResult.Ok();
            _feeRequirement.EnsureKcal(minAmount, (feeResult, error) =>
            {
                if (feeResult != PromptResult.Success)
                {
                    result = ValidationResult.Fail(string.IsNullOrEmpty(error) ? "KCAL is required to make transactions!" : error);
                }
            });

            return result;
        }
    }
}
