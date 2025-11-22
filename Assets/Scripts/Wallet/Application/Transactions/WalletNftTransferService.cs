using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds NFT transfer drafts so UI layers stay thin.
    /// </summary>
    public sealed class WalletNftTransferService
    {
        private readonly Func<AccountManager> _accountProvider;
        private readonly WalletNftTransactionBuilder _nftBuilder;
        private readonly WalletFeeRequirement _feeRequirement;

        public WalletNftTransferService(Func<AccountManager> accountProvider, WalletNftTransactionBuilder nftBuilder, WalletFeeRequirement feeRequirement)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _nftBuilder = nftBuilder ?? throw new ArgumentNullException(nameof(nftBuilder));
            _feeRequirement = feeRequirement ?? throw new ArgumentNullException(nameof(feeRequirement));
        }

        public WalletTransactionDraftResult BuildNftTransferDraft(string symbol, string destinationAddress, IEnumerable<string> nftIds)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return WalletTransactionDraftResult.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return WalletTransactionDraftResult.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return WalletTransactionDraftResult.Fail("Account state is unavailable.");
            }

            Address source;
            Address destination;

            try
            {
                source = Address.Parse(state.address);
            }
            catch
            {
                return WalletTransactionDraftResult.Fail("Invalid source address.");
            }

            try
            {
                destination = Address.Parse(destinationAddress);
            }
            catch
            {
                return WalletTransactionDraftResult.Fail("Invalid destination address.");
            }

            if (source == destination)
            {
                return WalletTransactionDraftResult.Fail("Source and destination address must be different.");
            }

            var ids = nftIds?.Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
            if (ids.Count == 0)
            {
                return WalletTransactionDraftResult.Fail("No NFTs selected for transfer.");
            }

            var feeCheck = EnsureKcal(accountManager, 0.1m);
            if (!feeCheck.Success)
            {
                return feeCheck;
            }

            try
            {
                var scripts = _nftBuilder.BuildTransferScripts(symbol, source, destination, ids, out var description);
                var plan = WalletTransactionDraft.ForScripts(description, scripts, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
                return WalletTransactionDraftResult.CreateSuccess(plan, ids.Count);
            }
            catch (Exception e)
            {
                return WalletTransactionDraftResult.Fail($"Failed to build NFT transfer transaction.\n{e.Message}");
            }
        }

        private WalletTransactionDraftResult EnsureKcal(AccountManager accountManager, decimal minAmount)
        {
            var result = WalletTransactionDraftResult.CreateSuccess(null);
            _feeRequirement.EnsureKcal(minAmount, (feeResult, error) =>
            {
                if (feeResult != PromptResult.Success)
                {
                    result = WalletTransactionDraftResult.Fail(string.IsNullOrEmpty(error) ? "KCAL is required to make transactions!" : error);
                }
            });
            return result;
        }
    }
}
