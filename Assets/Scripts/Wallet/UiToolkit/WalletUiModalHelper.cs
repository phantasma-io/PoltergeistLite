using System;
using System.Threading.Tasks;
using Poltergeist.Wallet;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Shared helpers for common modal patterns (info, confirm, errors) built on top of WalletUiModalHost.
    /// </summary>
    public static class WalletUiModalHelper
    {
        public static Task<(PromptResult result, string input)> ShowInfoAsync(WalletUiModalHost host, string title, string message, Action onBeforeShow = null, Action onAfterHide = null)
        {
            if (host == null)
            {
                return Task.FromResult((PromptResult.Failure, string.Empty));
            }

            return host.ShowPromptAsync(
                string.IsNullOrWhiteSpace(title) ? "Info" : title,
                message ?? string.Empty,
                0,
                0,
                allowEmpty: true,
                hasInput: false,
                isPassword: false,
                multiline: false,
                primaryLabel: "Close",
                secondaryLabel: "Cancel",
                showSecondary: false,
                onBeforeShow: onBeforeShow,
                onAfterHide: onAfterHide);
        }

        public static async Task<PromptResult> ShowConfirmAsync(WalletUiModalHost host, string title, string message, string confirmLabel = "Confirm", string cancelLabel = "Cancel", Action onBeforeShow = null, Action onAfterHide = null)
        {
            if (host == null)
            {
                return PromptResult.Failure;
            }

            var (result, _) = await host.ShowPromptAsync(
                string.IsNullOrWhiteSpace(title) ? "Confirm" : title,
                message ?? string.Empty,
                0,
                0,
                allowEmpty: true,
                hasInput: false,
                isPassword: false,
                multiline: false,
                primaryLabel: string.IsNullOrWhiteSpace(confirmLabel) ? "Confirm" : confirmLabel,
                secondaryLabel: string.IsNullOrWhiteSpace(cancelLabel) ? "Cancel" : cancelLabel,
                showSecondary: true,
                onBeforeShow: onBeforeShow,
                onAfterHide: onAfterHide);

            return result;
        }

        public static Task<(PromptResult result, string input)> ShowErrorAsync(WalletUiModalHost host, string title, string message, Action onBeforeShow = null, Action onAfterHide = null)
        {
            if (host == null)
            {
                return Task.FromResult((PromptResult.Failure, string.Empty));
            }

            return host.ShowPromptAsync(
                string.IsNullOrWhiteSpace(title) ? "Error" : title,
                message ?? string.Empty,
                0,
                0,
                allowEmpty: true,
                hasInput: false,
                isPassword: false,
                multiline: false,
                primaryLabel: "Close",
                secondaryLabel: "Cancel",
                showSecondary: true,
                successResult: PromptResult.Failure,
                cancelResult: PromptResult.Failure,
                onBeforeShow: onBeforeShow,
                onAfterHide: onAfterHide);
        }
    }
}
