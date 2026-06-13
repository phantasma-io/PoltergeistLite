using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist;

namespace Poltergeist.Wallet
{
    public interface IWalletTransactionUi
    {
        Task<PromptResult> RequestPasswordAsync(string description, PlatformKind platform);
        Task<PromptResult> ShowSendProgressAsync(string description, int txCount);
        void PushSendingState();
        void PopSendingState();
        Task<(Hash hash, TransactionResult txResult, string error)> ShowConfirmationAsync(Hash hash, bool refreshBalanceAfterConfirmation);
        Task ShowErrorAsync(string message);
    }

    /// <summary>
    /// Coordinates password prompt, send progress, transaction submission and confirmation on top of drafts.
    /// </summary>
    public sealed class WalletTransactionOrchestrator
    {
        private readonly Func<AccountManager> _accountProvider;
        private readonly IWalletTransactionUi _ui;

        public WalletTransactionOrchestrator(Func<AccountManager> accountProvider, IWalletTransactionUi ui)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        // Callback shim for callers that still expose completion through callbacks.
        public void SendTransactionDraft(WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            async void ExecuteAsync()
            {
                try
                {
                    var result = await SendTransactionDraftAsync(draft, refreshBalanceAfterConfirmation);
                    callback?.Invoke(result.hash, result.txResult, result.error);
                }
                catch (Exception ex)
                {
                    // Nothing above this awaits inside a guard, so a throw here would escape an
                    // async void and crash the process; report it through the callback instead.
                    Log.WriteWarning($"Transaction send failed: {ex}");
                    callback?.Invoke(Hash.Null, null, ex.Message);
                }
            }

            ExecuteAsync();
        }

        public async Task<(Hash hash, TransactionResult txResult, string error)> SendTransactionDraftAsync(WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation)
        {
            if (draft == null)
            {
                await _ui.ShowErrorAsync("Invalid transaction draft.");
                return (Hash.Null, null, "Invalid transaction draft.");
            }

            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                await _ui.ShowErrorAsync("Account manager is not available yet.");
                return (Hash.Null, null, "Account manager is not available yet.");
            }

            var scripts = BuildScriptList(draft);
            var description = AppendEstimatedFeeIfNeeded(accountManager, draft, scripts);

            var auth = await _ui.RequestPasswordAsync(description, accountManager.CurrentPlatform);
            if (auth != PromptResult.Success)
            {
                if (auth == PromptResult.Failure)
                {
                    await _ui.ShowErrorAsync("Authorization failed.");
                    return (Hash.Null, null, "Authorization failed.");
                }

                return (Hash.Null, null, null); // cancelled
            }

            var txCount = draft.IsCarbonTransaction ? 1 : scripts.Count;
            var prepResult = await _ui.ShowSendProgressAsync(BuildPreparingCaption(description, txCount), txCount);
            if (prepResult != PromptResult.Success)
            {
                return (Hash.Null, null, null); // cancelled
            }

            _ui.PushSendingState();
            try
            {
                if (draft.IsCarbonTransaction && draft.CarbonTx.HasValue)
                {
                    return await SendCarbonAsync(accountManager, draft, refreshBalanceAfterConfirmation);
                }

                if (scripts.Count > 1)
                {
                    return await SendMultipleScriptsAsync(accountManager, draft, scripts, refreshBalanceAfterConfirmation);
                }

                if (scripts.Count == 1)
                {
                    return await SendSingleScriptAsync(accountManager, draft, scripts[0], refreshBalanceAfterConfirmation);
                }

                await _ui.ShowErrorAsync("Transaction draft does not contain any scripts.");
                return (Hash.Null, null, "Transaction draft does not contain any scripts.");
            }
            finally
            {
                _ui.PopSendingState();
            }
        }

        private static string BuildPreparingCaption(string description, int txCount)
        {
            return txCount > 1
                ? $"Preparing {txCount} transactions...\n{description}"
                : $"Preparing transaction...\n{description}";
        }

        private List<byte[]> BuildScriptList(WalletTransactionDraft draft)
        {
            if (draft.Script != null)
            {
                return new List<byte[]> { draft.Script };
            }

            if (draft.Scripts != null && draft.Scripts.Count > 0)
            {
                return draft.Scripts.ToList();
            }

            return new List<byte[]>();
        }

        private string AppendEstimatedFeeIfNeeded(AccountManager accountManager, WalletTransactionDraft draft, IReadOnlyList<byte[]> scripts)
        {
            var description = draft.Description;

            if (accountManager.CurrentPlatform == PlatformKind.Phantasma && scripts.Count > 0)
            {
                BigInteger usedGas = 0;
                foreach (var script in scripts)
                {
                    try
                    {
                        var vm = new GasMachine(script, 0, null);
                        vm.Execute();
                        usedGas += vm.UsedGas;
                    }
                    catch
                    {
                        usedGas += 400;
                    }
                }

                var estimatedFee = usedGas * draft.GasPrice;
                var feeDecimals = Tokens.GetTokenDecimals("KCAL", accountManager.CurrentPlatform);
                description += $"\nEstimated fee: {WalletAmountFormatter.Format(estimatedFee, feeDecimals)} KCAL";
            }

            return description;
        }

        private async Task<(Hash hash, TransactionResult txResult, string error)> SendCarbonAsync(AccountManager accountManager, WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation)
        {
            var sendResult = await SignAndSendCarbonAsync(accountManager, draft.CarbonTx.Value);
            if (string.IsNullOrEmpty(sendResult.error))
            {
                return await _ui.ShowConfirmationAsync(sendResult.hash, refreshBalanceAfterConfirmation);
            }

            await _ui.ShowErrorAsync("Cannot send transaction. Details:\n" + sendResult.error);
            return (sendResult.hash == Hash.Null ? Hash.Null : sendResult.hash, null, sendResult.error);
        }

        private async Task<(Hash hash, TransactionResult txResult, string error)> SendSingleScriptAsync(AccountManager accountManager, WalletTransactionDraft draft, byte[] script, bool refreshBalanceAfterConfirmation)
        {
            var sendResult = await SignAndSendTransactionAsync(accountManager, draft.Chain, script, draft.Payload);
            if (string.IsNullOrEmpty(sendResult.error))
            {
                return await _ui.ShowConfirmationAsync(sendResult.hash, refreshBalanceAfterConfirmation);
            }

            var errorText = string.IsNullOrEmpty(sendResult.error) ? "Unknown error." : sendResult.error;
            await _ui.ShowErrorAsync(errorText);
            return (Hash.Null, null, errorText);
        }

        private async Task<(Hash hash, TransactionResult txResult, string error)> SendMultipleScriptsAsync(AccountManager accountManager, WalletTransactionDraft draft, IReadOnlyList<byte[]> scripts, bool refreshBalanceAfterConfirmation)
        {
            if (scripts.Count == 0)
            {
                await _ui.ShowErrorAsync("Transaction draft does not contain any scripts.");
                return (Hash.Null, null, "Transaction draft does not contain any scripts.");
            }

            var sendResult = await SignAndSendTransactionAsync(accountManager, draft.Chain, scripts[0], draft.Payload);
            if (string.IsNullOrEmpty(sendResult.error) && sendResult.hash != Hash.Null)
            {
                var isLast = scripts.Count == 1;
                var confirmation = await _ui.ShowConfirmationAsync(sendResult.hash, isLast && refreshBalanceAfterConfirmation);
                if (!string.IsNullOrEmpty(confirmation.error) || isLast)
                {
                    return confirmation;
                }

                return await SendMultipleScriptsAsync(accountManager, draft, scripts.Skip(1).ToList(), refreshBalanceAfterConfirmation);
            }

            var errorText = string.IsNullOrEmpty(sendResult.error) ? "Error sending transaction." : sendResult.error;
            await _ui.ShowErrorAsync(errorText);
            return (Hash.Null, null, errorText);
        }

        private static Task<(Hash hash, string error)> SignAndSendTransactionAsync(AccountManager accountManager, string chain, byte[] script, byte[] payload)
        {
            var tcs = new TaskCompletionSource<(Hash hash, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountManager.SignAndSendTransaction(chain, script, payload, (hash, error) => tcs.TrySetResult((hash, error)));
            return tcs.Task;
        }

        private static Task<(Hash hash, string error)> SignAndSendCarbonAsync(AccountManager accountManager, TxMsg tx)
        {
            var tcs = new TaskCompletionSource<(Hash hash, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountManager.SignAndSendCarbonTransaction(tx, (hash, error) => tcs.TrySetResult((hash, error)));
            return tcs.Task;
        }
    }
}
