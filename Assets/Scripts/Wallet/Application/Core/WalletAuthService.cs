using System;
using System.Threading.Tasks;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.Wallet
{
    public interface IWalletAuthUi
    {
        Task<(PromptResult result, string password)> PromptPasswordAsync(string title, string caption, int minLength, int maxLength);
        Task ShowErrorAsync(string message);
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

        public async Task<PromptResult> RequestPasswordAsync(string description, PlatformKind platform, bool forcePasswordPrompt, bool allowMasterPasswordPrompt, IWalletAuthUi ui, bool ignoreStoredPassword = false)
        {
            var accountManager = _accountProvider();
            if (accountManager == null || ui == null)
            {
                return PromptResult.Failure;
            }

            if (!accountManager.HasSelection)
            {
                return PromptResult.Failure;
            }

            if (!accountManager.CurrentAccount.passwordProtected)
            {
                return PromptResult.Success;
            }

            if (!forcePasswordPrompt && accountManager.Settings.passwordMode == PasswordMode.Ask_Only_On_Login)
            {
                return PromptResult.Success;
            }

            while (true)
            {
                if (accountManager.Settings.passwordMode == PasswordMode.Master_Password &&
                    allowMasterPasswordPrompt &&
                    string.IsNullOrEmpty(_masterPassword))
                {
                    var masterPrompt = await ui.PromptPasswordAsync("Master Password", "Please enter master password", AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength);
                    if (masterPrompt.result == PromptResult.Success)
                    {
                        _masterPassword = masterPrompt.password;
                    }
                    else
                    {
                        allowMasterPasswordPrompt = false;
                        continue;
                    }
                }

                var passwordToTry = !ignoreStoredPassword && !string.IsNullOrEmpty(_masterPassword) ? _masterPassword : null;
                if (string.IsNullOrEmpty(passwordToTry))
                {
                    var prompt = await ui.PromptPasswordAsync("Account Authorization", $"Account: {accountManager.CurrentAccount.name}\nAction: {description}\n\nInsert password to proceed...", AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength);
                    if (prompt.result != PromptResult.Success)
                    {
                        return prompt.result;
                    }

                    passwordToTry = prompt.password;
                }

                var tryResult = TryPassword(passwordToTry, accountManager);
                if (tryResult == PromptResult.Success)
                {
                    return PromptResult.Success;
                }

                await ui.ShowErrorAsync($"Incorrect password for '{accountManager.CurrentAccount.name}' account.");
            }
        }

        public async void RequestPassword(string description, PlatformKind platform, bool forcePasswordPrompt, bool allowMasterPasswordPrompt, IWalletAuthUi ui, Action<PromptResult> callback, bool ignoreStoredPassword = false)
        {
            // Legacy callback shim: kept to avoid touching legacy callers while the UI migrates to async.
            PromptResult result;
            try
            {
                result = await RequestPasswordAsync(description, platform, forcePasswordPrompt, allowMasterPasswordPrompt, ui, ignoreStoredPassword);
            }
            catch (Exception e)
            {
                Log.WriteWarning("Authorization error: " + e);
                result = PromptResult.Failure;
            }

            callback?.Invoke(result);
        }

        private PromptResult TryPassword(string password, AccountManager accountManager)
        {
            if (accountManager == null)
            {
                return PromptResult.Failure;
            }

            try
            {
                AccountManager.GetPasswordHashBySalt(password, accountManager.CurrentAccount.passwordIterations, accountManager.CurrentAccount.salt, out string passwordHash);
                var wif = AccountManager.DecryptString(accountManager.CurrentAccount.WIF, passwordHash, accountManager.CurrentAccount.iv);

                if (PhantasmaKeys.FromWIF(wif).Address.ToString() == accountManager.CurrentAccount.phaAddress)
                {
                    accountManager.CurrentPasswordHash = passwordHash;
                    accountManager.UpdateOpenAccount();
                    return PromptResult.Success;
                }
            }
            catch (Exception e)
            {
                Log.WriteWarning("Authorization error: " + e);
            }

            _masterPassword = null;
            return PromptResult.Failure;
        }

    }
}
