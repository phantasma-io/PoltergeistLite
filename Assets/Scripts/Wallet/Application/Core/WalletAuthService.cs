using System;
using PhantasmaPhoenix.Cryptography;

namespace Poltergeist.Wallet
{
    public interface IWalletAuthUi
    {
        void PromptPassword(string title, string caption, int minLength, int maxLength, Action<PromptResult, string> callback);
        void ShowError(string message, Action onClosed);
    }

    /// <summary>
    /// Handles wallet password authorization with optional master password caching.
    /// Keeps UI interactions behind an adapter so different UI layers can reuse the logic.
    /// </summary>
    public sealed class WalletAuthService
    {
        private readonly Func<AccountManager> _accountProvider;
        private string _masterPassword;

        public WalletAuthService(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public void ClearCachedMasterPassword()
        {
            _masterPassword = null;
        }

        public void RequestPassword(string description, PlatformKind platform, bool forcePasswordPrompt, bool allowMasterPasswordPrompt, IWalletAuthUi ui, Action<PromptResult> callback, bool ignoreStoredPassword = false)
        {
            var accountManager = _accountProvider();
            if (accountManager == null || ui == null)
            {
                callback?.Invoke(PromptResult.Failure);
                return;
            }

            if (!accountManager.HasSelection)
            {
                callback?.Invoke(PromptResult.Failure);
                return;
            }

            if (!accountManager.CurrentAccount.passwordProtected)
            {
                callback?.Invoke(PromptResult.Success);
                return;
            }

            if (!forcePasswordPrompt && accountManager.Settings.passwordMode == PasswordMode.Ask_Only_On_Login)
            {
                callback?.Invoke(PromptResult.Success);
                return;
            }

            void PromptForPassword()
            {
                ui.PromptPassword("Account Authorization", $"Account: {accountManager.CurrentAccount.name}\nAction: {description}\n\nInsert password to proceed...", AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        TryPassword(input, description, platform, forcePasswordPrompt, allowMasterPasswordPrompt, ui, callback);
                    }
                });
            }

            void ProceedWithCheck()
            {
                if (!ignoreStoredPassword && !string.IsNullOrEmpty(_masterPassword))
                {
                    TryPassword(_masterPassword, description, platform, forcePasswordPrompt, allowMasterPasswordPrompt, ui, callback);
                }
                else
                {
                    PromptForPassword();
                }
            }

            if (accountManager.Settings.passwordMode == PasswordMode.Master_Password &&
                string.IsNullOrEmpty(_masterPassword) &&
                allowMasterPasswordPrompt)
            {
                ui.PromptPassword("Master Password", "Please enter master password", AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        _masterPassword = input;
                        ProceedWithCheck();
                    }
                    else
                    {
                        RequestPassword(description, platform, forcePasswordPrompt, false, ui, callback);
                    }
                });
            }
            else
            {
                ProceedWithCheck();
            }
        }

        private void TryPassword(string password, string description, PlatformKind platform, bool forcePasswordPrompt, bool allowMasterPasswordPrompt, IWalletAuthUi ui, Action<PromptResult> callback)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                callback?.Invoke(PromptResult.Failure);
                return;
            }

            try
            {
                AccountManager.GetPasswordHashBySalt(password, accountManager.CurrentAccount.passwordIterations, accountManager.CurrentAccount.salt, out string passwordHash);
                var wif = AccountManager.DecryptString(accountManager.CurrentAccount.WIF, passwordHash, accountManager.CurrentAccount.iv);

                if (PhantasmaKeys.FromWIF(wif).Address.ToString() == accountManager.CurrentAccount.phaAddress)
                {
                    accountManager.CurrentPasswordHash = passwordHash;
                    accountManager.UpdateOpenAccount();
                    callback?.Invoke(PromptResult.Success);
                    return;
                }
            }
            catch (Exception e)
            {
                PhantasmaPhoenix.Unity.Core.Logging.Log.WriteWarning("Authorization error: " + e);
            }

            _masterPassword = null;
            ui.ShowError($"Incorrect password for '{accountManager.CurrentAccount.name}' account.", () =>
            {
                RequestPassword(description, platform, forcePasswordPrompt, allowMasterPasswordPrompt, ui, callback);
            });
        }
    }
}
