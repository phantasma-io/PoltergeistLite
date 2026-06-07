using System;
using System.Threading.Tasks;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC.Models;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// UI-agnostic bridge that allows services (e.g. WalletLink) to request user interaction.
    /// A concrete UI layer must register an implementation at startup.
    /// </summary>
    public interface IWalletUiBridge
    {
        void PostToMainThread(Action action);
        Task<bool> ConfirmAsync(string title, string text, string confirmLabel = "Yes", string cancelLabel = "No");
        Task<bool> PromptAsync(string text);
        Task<(Hash hash, TransactionResult txResult, string error)> SendTransactionDraftAsync(WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation = true);
        void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successCustomMessage = null, string failureCustomMessage = null);
        Task<(string[] result, string error)> InvokeScriptAsync(string chain, byte[] script);
        Task<(bool success, string error)> WriteArchiveAsync(Hash hash, int blockIndex, byte[] data);
    }

    public static class WalletUiBridge
    {
        public static IWalletUiBridge Current { get; private set; }

        public static void Register(IWalletUiBridge bridge)
        {
            Current = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public static void Unregister(IWalletUiBridge bridge)
        {
            if (bridge == null)
            {
                return;
            }

            if (ReferenceEquals(Current, bridge))
            {
                Current = null;
            }
        }
    }
}
