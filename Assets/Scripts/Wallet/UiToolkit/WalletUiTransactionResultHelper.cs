using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Models;
using Poltergeist.Build;
using Poltergeist.Wallet;
using UnityEngine;

namespace Poltergeist.UiToolkit
{
    internal static class WalletUiTransactionResultHelper
    {
        internal const string PendingNotice = "The transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance";

        internal static string CombineWithPendingNotice(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return PendingNotice;
            }

            return $"{message}\n\n{PendingNotice}";
        }

        internal static Task ShowAsync(WalletUiModalHost modalHost, Func<AccountManager> accountProvider, Hash hash, TransactionResult txResult, string error, string successCustomMessage = null, string failureCustomMessage = null)
        {
            if (modalHost == null)
            {
                throw new ArgumentNullException(nameof(modalHost));
            }

            if (accountProvider == null)
            {
                throw new ArgumentNullException(nameof(accountProvider));
            }

            return ShowAsyncInternal(modalHost, accountProvider, hash, txResult, error, successCustomMessage, failureCustomMessage);
        }

        private static async Task ShowAsyncInternal(WalletUiModalHost modalHost, Func<AccountManager> accountProvider, Hash hash, TransactionResult txResult, string error, string successCustomMessage, string failureCustomMessage)
        {
            if (hash == Hash.Null && txResult == null && string.IsNullOrEmpty(error))
            {
                return;
            }

            var success = string.IsNullOrEmpty(error) && hash != Hash.Null;
            var timeout = error == "timeout";
            var message = new StringBuilder();
            var printDetails = false;

            if (success && !string.IsNullOrEmpty(successCustomMessage))
            {
                message.Append(successCustomMessage);
            }
            else if (!success && hash == Hash.Null)
            {
                // Hash unavailable: fallback to error messaging below.
            }
            else if (!success && !string.IsNullOrEmpty(failureCustomMessage))
            {
                message.Append(failureCustomMessage);
            }
            else
            {
                if (success)
                {
                    message.Append(PendingNotice);
                }
                else if (timeout)
                {
                    message.Append("Your transaction has been broadcasted but its state cannot be determined.\nPlease use explorer to ensure transaction is confirmed successfully and funds are transferred (button 'View' below).\n");
                }
            }

            if (!string.IsNullOrEmpty(error) && !timeout)
            {
                if (message.Length > 0)
                {
                    message.Append("\n");
                }

                message.Append("Error: ").Append(AddIndent(error, "    "));

                if (txResult != null)
                {
                    message.Append("\nResult: ").Append(txResult.Result);
                    message.Append("\nComment: ").Append(txResult.DebugComment);
                }

                printDetails = true;
            }

            if (hash != Hash.Null)
            {
                if (message.Length > 0)
                {
                    message.Append("\n");
                }

                message.Append("Transaction hash:\n").Append(hash);
            }

            if (printDetails)
            {
                var details = BuildAdditionalDetails(accountProvider());
                if (!string.IsNullOrEmpty(details))
                {
                    message.Append(details);
                }
            }

            var explorerUrl = BuildExplorerUrl(accountProvider(), hash);
            var (result, _) = await WalletUiModalHelper.ShowPromptAsync(
                modalHost,
                success ? "Success" : (timeout ? "Attention" : "Failure"),
                message.ToString(),
                0,
                0,
                allowEmpty: true,
                hasInput: false,
                isPassword: false,
                multiline: true,
                primaryLabel: "Close",
                secondaryLabel: string.IsNullOrEmpty(explorerUrl) ? "Cancel" : "View",
                showSecondary: !string.IsNullOrEmpty(explorerUrl),
                successResult: PromptResult.Success,
                cancelResult: PromptResult.Failure);

            if (result == PromptResult.Failure && !string.IsNullOrEmpty(explorerUrl))
            {
                Application.OpenURL(explorerUrl);
            }
        }

        private static string BuildExplorerUrl(AccountManager accountManager, Hash hash)
        {
            if (accountManager == null || hash == Hash.Null)
            {
                return null;
            }

            if (accountManager.CurrentPlatform == PlatformKind.Phantasma)
            {
                return accountManager.GetPhantasmaTransactionURL(hash.ToString());
            }

            return null;
        }

        private static string BuildAdditionalDetails(AccountManager accountManager)
        {
            if (accountManager == null || accountManager.Settings == null)
            {
                return string.Empty;
            }

            var settings = accountManager.Settings;
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("Details:");
            sb.AppendLine($"  Wallet version: {Application.version} built on: {Info.Instance.BuildTime} UTC");
            sb.AppendLine($"  Nexus: {settings.nexusName}");
            sb.AppendLine($"  RPC: {settings.phantasmaRPCURL}");
            sb.AppendLine($"  Fee price: {settings.feePrice}");
            sb.AppendLine($"  Fee limit: {settings.feeLimit}");

            if (settings.devMode)
            {
                sb.AppendLine($"  Developer mode: {settings.devMode}");
                sb.AppendLine($"  No validation mode: {settings.devMode_NoValidation}");
            }

            return sb.ToString();
        }

        private static string AddIndent(string input, string indent)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var lines = input.Split('\n');
            return string.Join("\n", lines.Select((line, index) => index == 0 ? line : indent + line));
        }
    }
}
