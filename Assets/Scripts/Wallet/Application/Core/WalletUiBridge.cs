using System;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC.Models;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// UI-agnostic bridge that allows services (e.g. WalletLink) to request user interaction without hard-coding legacy IMGUI.
    /// A concrete UI layer must register an implementation at startup.
    /// </summary>
    public interface IWalletUiBridge
    {
        void PostToMainThread(Action action);
        void Prompt(string text, Action<bool> callback);
        void SendTransactionDraft(WalletTransactionDraft draft, Action<Hash, TransactionResult, string> callback, bool refreshBalanceAfterConfirmation = true);
        void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successCustomMessage = null, string failureCustomMessage = null);
        void InvokeScript(string chain, byte[] script, Action<string[], string> callback);
        void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback);
    }

    public static class WalletUiBridge
    {
        public static IWalletUiBridge Current { get; private set; }

        public static void Register(IWalletUiBridge bridge)
        {
            Current = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }
    }
}
