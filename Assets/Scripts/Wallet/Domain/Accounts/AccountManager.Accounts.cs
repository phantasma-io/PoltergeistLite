using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.VM;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Core.Extensions;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.NFT;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Unity.Core.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Numerics;

namespace Poltergeist
{
    public partial class AccountManager
    {
        private bool IsNameTaken(string name)
        {
            return Accounts.Any(account => account.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // Derive every public chain address this wallet exposes from one WIF, in a single place so
        // the add and replace paths cannot drift apart.
        private static void AssignAddressesFromWif(ref Account account, string wif)
        {
            account.phaAddress = PhantasmaKeys.FromWIF(wif).Address.ToString();
            account.neoAddress = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif).Address.ToString();

            var ethAddress = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif).Address;
            account.ethAddress = new PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util.AddressUtil().ConvertToChecksumAddress(ethAddress);
        }

        public int AddWallet(string name, string wif, string password, bool legacySeed)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3)
            {
                throw new Exception("Name is too short.");
            }

            if (name.Length > 16)
            {
                throw new Exception("Name is too long.");
            }

            if (IsNameTaken(name))
            {
                throw new Exception("An account with this name already exists.");
            }

            var account = new Account() { name = name, platforms = AccountManager.MergeAvailablePlatforms(), misc = "" };

            // Initializing public addresses.
            AssignAddressesFromWif(ref account, wif);

            if (!String.IsNullOrEmpty(password))
            {
                account.passwordProtected = true;
                account.passwordIterations = PasswordIterations;

                // Encrypting WIF.
                GetPasswordHash(password, account.passwordIterations, out string salt, out string passwordHash);
                account.password = "";
                account.salt = salt;

                account.WIF = EncryptString(wif, passwordHash, out string iv);
                account.iv = iv;

                // Decrypting to ensure there are no exceptions.
                DecryptString(account.WIF, passwordHash, account.iv);
            }
            else
            {
                account.passwordProtected = false;
                account.WIF = wif;
            }

            account.misc = legacySeed ? "legacy-seed" : "";

            Accounts.Add(account);

            return Accounts.Count() - 1;
        }

        internal void DeleteAccount(int currentIndex)
        {
            if (currentIndex < 0 || currentIndex >= Accounts.Count())
            {
                return;
            }

            Accounts.RemoveAt(currentIndex);
            SaveAccounts();
        }

        internal void ReplaceAccountWIF(int currentIndex, string wif, string passwordHash, out string deletedDuplicateWallet)
        {
            deletedDuplicateWallet = null;

            if (currentIndex < 0 || currentIndex >= Accounts.Count())
            {
                return;
            }

            var account = Accounts[currentIndex];
            if (string.IsNullOrEmpty(passwordHash))
            {
                account.WIF = wif;
            }
            else
            {
                account.WIF = EncryptString(wif, passwordHash, out string iv);
                account.iv = iv;
            }
            account.misc = ""; // Migration does not guarantee that new account have current seed, but that's all that we can do with it.

            // Initializing new public addresses.
            wif = account.GetWif(passwordHash); // Recreating to be sure all is good.
            AssignAddressesFromWif(ref account, wif);

            Accounts[currentIndex] = account;

            for (var i = 0; i < Accounts.Count; i++)
            {
                if (i != currentIndex && Accounts[i].phaAddress == account.phaAddress)
                {
                    deletedDuplicateWallet = Accounts[i].name;
                    Accounts.RemoveAt(i);
                    break;
                }
            }

            SaveAccounts();
        }

        public bool RenameAccount(string newName)
        {
            if (IsNameTaken(newName))
            {
                return true;
            }

            var account = Accounts[CurrentIndex];
            account.name = newName;
            Accounts[CurrentIndex] = account;
            SaveAccounts();
            return true;
        }

        private void LogTaskException(Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            Log.WriteWarning(ex.ToString());
        }

        internal void ValidateAccountName(string name, Action<string> callback)
        {
            async Task ExecuteAsync()
            {
                try
                {
                    var address = await AsyncPhantasma.FromApi<string>(
                        (onSuccess, onError) => phantasmaApi.LookUpName(
                            name,
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        CancellationToken.None);
                    callback(address);
                }
                catch (PhantasmaRequestException ex)
                {
                    RotateRpcOnWebError(ex);
                    callback(null);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public string GetAddress(int index, PlatformKind platform)
        {
            if (index < 0 || index >= Accounts.Count())
            {
                return null;
            }

            // For the active account the live per-platform state holds the authoritative address
            // (it can differ from the stored field after an in-session refresh); otherwise read it
            // straight off the stored account.
            if (index == _selectedAccountIndex && _states.TryGetValue(platform, out var state))
            {
                return state.address;
            }

            var account = Accounts[index];
            return platform switch
            {
                PlatformKind.Phantasma => account.phaAddress,
                PlatformKind.Neo => account.neoAddress,
                PlatformKind.Ethereum or PlatformKind.BSC => account.ethAddress,
                _ => null,
            };
        }

        public void GetPhantasmaAddressInfo(string addressString, Account? account, Action<string, string> callback)
        {
            byte[] scriptUnclaimed;
            byte[] scriptStake;
            byte[] scriptStorageStake;
            byte[] scriptVotingPower;
            byte[] scriptStakeTimestamp;
            byte[] scriptTimeBeforeUnstake;
            byte[] scriptMasterDate;
            byte[] scriptIsMaster;
            try
            {
                var address = Address.Parse(addressString);

                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetUnclaimed", address);
                    scriptUnclaimed = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetStake", address);
                    scriptStake = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetStorageStake", address);
                    scriptStorageStake = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetAddressVotingPower", address);
                    scriptVotingPower = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetStakeTimestamp", address);
                    scriptStakeTimestamp = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetTimeBeforeUnstake", address);
                    scriptTimeBeforeUnstake = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetMasterDate", address);
                    scriptMasterDate = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "IsMaster", address);
                    scriptIsMaster = sb.EndScript();
                }
            }
            catch (Exception e)
            {
                callback(null, e.ToString());
                return;
            }

            InvokeScriptPhantasma("main", scriptUnclaimed, (unclaimedResult, unclaimedInvokeError) =>
            {
                if (!string.IsNullOrEmpty(unclaimedInvokeError))
                {
                    callback(null, "Script invocation error!\n\n" + unclaimedInvokeError);
                    return;
                }
                else
                {
                    InvokeScriptPhantasma("main", scriptStake, (stakeResult, stakeInvokeError) =>
                    {
                        if (!string.IsNullOrEmpty(stakeInvokeError))
                        {
                            callback(null, "Script invocation error!\n\n" + stakeInvokeError);
                            return;
                        }
                        else
                        {
                            InvokeScriptPhantasma("main", scriptStorageStake, (storageStakeResult, storageStakeInvokeError) =>
                            {
                                if (!string.IsNullOrEmpty(storageStakeInvokeError))
                                {
                                    callback(null, "Script invocation error!\n\n" + storageStakeInvokeError);
                                    return;
                                }
                                else
                                {
                                    InvokeScriptPhantasma("main", scriptVotingPower, (votingPowerResult, votingPowerInvokeError) =>
                                    {
                                        if (!string.IsNullOrEmpty(votingPowerInvokeError))
                                        {
                                            callback(null, "Script invocation error!\n\n" + votingPowerInvokeError);
                                            return;
                                        }
                                        else
                                        {
                                            InvokeScriptPhantasma("main", scriptStakeTimestamp, (stakeTimestampResult, stakeTimestampInvokeError) =>
                                            {
                                                if (!string.IsNullOrEmpty(stakeTimestampInvokeError))
                                                {
                                                    callback(null, "Script invocation error!\n\n" + stakeTimestampInvokeError);
                                                    return;
                                                }
                                                else
                                                {
                                                    InvokeScriptPhantasma("main", scriptTimeBeforeUnstake, (timeBeforeUnstakeResult, timeBeforeUnstakeInvokeError) =>
                                                    {
                                                        if (!string.IsNullOrEmpty(timeBeforeUnstakeInvokeError))
                                                        {
                                                            callback(null, "Script invocation error!\n\n" + timeBeforeUnstakeInvokeError);
                                                            return;
                                                        }
                                                        else
                                                        {
                                                            InvokeScriptPhantasma("main", scriptMasterDate, (masterDateResult, masterDateInvokeError) =>
                                                            {
                                                                if (!string.IsNullOrEmpty(masterDateInvokeError))
                                                                {
                                                                    callback(null, "Script invocation error!\n\n" + masterDateInvokeError);
                                                                    return;
                                                                }
                                                                else
                                                                {
                                                                    InvokeScriptPhantasma("main", scriptIsMaster, (isMasterResult, isMasterInvokeError) =>
                                                                    {
                                                                        if (!string.IsNullOrEmpty(isMasterInvokeError))
                                                                        {
                                                                            callback(null, "Script invocation error!\n\n" + isMasterInvokeError);
                                                                            return;
                                                                        }
                                                                        else
                                                                        {
                                                                            var unclaimedRaw = unclaimedResult != null ? VMObject.FromBytes(unclaimedResult).AsNumber() : -1;
                                                                            var stakeRaw = stakeResult != null ? VMObject.FromBytes(stakeResult).AsNumber() : -1;
                                                                            var storageStakeRaw = storageStakeResult != null ? VMObject.FromBytes(storageStakeResult).AsNumber() : -1;
                                                                            var unclaimed = WalletAmountFormatter.Format(unclaimedRaw, 10);
                                                                            var stake = WalletAmountFormatter.Format(stakeRaw, 8);
                                                                            var storageStake = WalletAmountFormatter.Format(storageStakeRaw, 8);
                                                                            var votingPower = votingPowerResult != null ? VMObject.FromBytes(votingPowerResult).AsNumber() : -1;
                                                                            var stakeTimestamp = stakeTimestampResult != null ? VMObject.FromBytes(stakeTimestampResult).AsTimestamp() : 0;
                                                                            var stakeTimestampLocal = stakeTimestamp != null ? ((DateTime)stakeTimestamp).ToLocalTime() : DateTime.MinValue;
                                                                            var timeBeforeUnstake = timeBeforeUnstakeResult != null ? VMObject.FromBytes(timeBeforeUnstakeResult).AsNumber() : -1;
                                                                            var masterDate = masterDateResult != null ? VMObject.FromBytes(masterDateResult).AsTimestamp() : 0;
                                                                            var isMaster = isMasterResult != null ? VMObject.FromBytes(isMasterResult).AsBool() : false;

                                                                            callback($"{addressString} account information:\n\n" +
                                                                                $"Unclaimed: {unclaimed} KCAL\n" +
                                                                                $"Stake: {stake} SOUL\n" +
                                                                                $"Is SM: {isMaster}\n" +
                                                                                $"SM since: {masterDate}\n" +
                                                                                $"Stake timestamp: {stakeTimestampLocal} ({stakeTimestamp} UTC)\n" +
                                                                                $"Next staking period starts in: {TimeSpan.FromSeconds((double)timeBeforeUnstake):hh\\:mm\\:ss}\n" +
                                                                                $"Storage stake: {storageStake} SOUL\n" +
                                                                                $"Voting power: {votingPower}" +
                                                                                (account != null ? $"\n\nNeo legacy address: {((Account)account).neoAddress}\nN3 address: {((Account)account).neoAddressN3}\nEth/BSC address: {((Account)account).ethAddress}" : ""), null);
                                                                        }
                                                                    });
                                                                }
                                                            });
                                                        }
                                                    });
                                                }
                                            });
                                        }
                                    });
                                }
                            });
                        }
                    });
                }

            });
        }

    }
}
