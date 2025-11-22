using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.RPC.Models;
using Poltergeist;

namespace Poltergeist.Wallet
{
    public interface IWalletTransactionUi
    {
        void RequestPassword(string description, PlatformKind platform, Action<PromptResult> callback);
        void ShowSendProgress(string description, int txCount, Action<PromptResult> callback);
        void PushSendingState();
        void PopSendingState();
        void ShowConfirmation(Hash hash, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback);
        void ShowError(string message);
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

        public void SendDraft(WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            if (draft == null)
            {
                _ui.ShowError("Invalid transaction draft.");
                callback?.Invoke(Hash.Null, null, "Invalid transaction draft.");
                return;
            }

            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                _ui.ShowError("Account manager is not available yet.");
                callback?.Invoke(Hash.Null, null, "Account manager is not available yet.");
                return;
            }

            var scripts = BuildScriptList(draft);
            var description = AppendEstimatedFeeIfNeeded(accountManager, draft, scripts);

            _ui.RequestPassword(description, accountManager.CurrentPlatform, (auth) =>
            {
                if (auth != PromptResult.Success)
                {
                    if (auth == PromptResult.Failure)
                    {
                        _ui.ShowError("Authorization failed.");
                        callback?.Invoke(Hash.Null, null, "Authorization failed.");
                    }
                    else
                    {
                        callback?.Invoke(Hash.Null, null, null); // cancelled
                    }

                    return;
                }

                var txCount = draft.IsCarbonTransaction ? 1 : scripts.Count;
                _ui.ShowSendProgress(BuildPreparingCaption(description, txCount), txCount, (prepResult) =>
                {
                    if (prepResult != PromptResult.Success)
                    {
                        callback?.Invoke(Hash.Null, null, null); // cancelled
                        return;
                    }

                    _ui.PushSendingState();
                    if (draft.IsCarbonTransaction && draft.CarbonTx.HasValue)
                    {
                        SendCarbon(accountManager, draft, refreshBalanceAfterConfirmation, callback);
                    }
                    else if (scripts.Count > 1)
                    {
                        SendMultipleScripts(accountManager, draft, scripts, refreshBalanceAfterConfirmation, callback);
                    }
                    else if (scripts.Count == 1)
                    {
                        SendSingleScript(accountManager, draft, scripts[0], refreshBalanceAfterConfirmation, callback);
                    }
                    else
                    {
                        _ui.PopSendingState();
                        _ui.ShowError("Transaction draft does not contain any scripts.");
                        callback?.Invoke(Hash.Null, null, "Transaction draft does not contain any scripts.");
                    }
                });
            });
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
                description += $"\nEstimated fee: {UnitConversion.ToDecimal(estimatedFee, feeDecimals)} KCAL";
            }

            return description;
        }

        private void SendCarbon(AccountManager accountManager, WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            accountManager.SignAndSendCarbonTransaction(draft.CarbonTx.Value, (hash, error) =>
            {
                if (string.IsNullOrEmpty(error))
                {
                    _ui.ShowConfirmation(hash, refreshBalanceAfterConfirmation, callback);
                }
                else
                {
                    _ui.PopSendingState();
                    _ui.ShowError("Cannot send transaction. Details:\n" + error);
                    callback?.Invoke(hash == Hash.Null ? Hash.Null : hash, null, error);
                }
            });
        }

        private void SendSingleScript(AccountManager accountManager, WalletTransactionDraft draft, byte[] script, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            accountManager.SignAndSendTransaction(draft.Chain, script, draft.Payload, (hash, error) =>
            {
                if (string.IsNullOrEmpty(error))
                {
                    _ui.ShowConfirmation(hash, refreshBalanceAfterConfirmation, callback);
                }
                else
                {
                    _ui.PopSendingState();
                    _ui.ShowError(string.IsNullOrEmpty(error) ? "Unknown error." : error);
                    callback?.Invoke(Hash.Null, null, string.IsNullOrEmpty(error) ? "Unknown error." : error);
                }
            });
        }

        private void SendMultipleScripts(AccountManager accountManager, WalletTransactionDraft draft, IReadOnlyList<byte[]> scripts, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            if (scripts.Count == 0)
            {
                _ui.PopSendingState();
                _ui.ShowError("Transaction draft does not contain any scripts.");
                callback?.Invoke(Hash.Null, null, "Transaction draft does not contain any scripts.");
                return;
            }

            accountManager.SignAndSendTransaction(draft.Chain, scripts[0], draft.Payload, (hash, error) =>
            {
                if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                {
                    var isLast = scripts.Count == 1;
                    _ui.ShowConfirmation(hash, isLast && refreshBalanceAfterConfirmation, (txHash, txResult, confirmError) =>
                    {
                        if (!string.IsNullOrEmpty(confirmError))
                        {
                            callback?.Invoke(txHash, txResult, confirmError);
                            return;
                        }

                        if (isLast)
                        {
                            callback?.Invoke(txHash, txResult, confirmError);
                        }
                        else
                        {
                            SendMultipleScripts(accountManager, draft, scripts.Skip(1).ToList(), refreshBalanceAfterConfirmation, callback);
                        }
                    });
                }
                else
                {
                    _ui.PopSendingState();
                    _ui.ShowError(string.IsNullOrEmpty(error) ? "Error sending transaction." : error);
                    callback?.Invoke(Hash.Null, null, string.IsNullOrEmpty(error) ? "Error sending transaction." : error);
                }
            });
        }
    }
}
