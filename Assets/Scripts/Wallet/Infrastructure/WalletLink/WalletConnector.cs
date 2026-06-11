using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.VM;
using UnityEngine;
using Poltergeist.Wallet;

namespace Poltergeist
{
    public class WalletConnector : WalletLink, IWalletLinkV5Ops
    {
        private IWalletUiBridge Ui => WalletUiBridge.Current;

        public override string Nexus => AccountManager.Instance.Settings.nexusName;
        public override string Name => "Poltergeist Lite";

        protected override WalletStatus Status => AccountManager.Instance.CurrentState != null ? WalletStatus.Ready : WalletStatus.Closed;

        public WalletConnector() : base()
        {
        }

        private void RunOnUi(Action action)
        {
            var ui = Ui;
            if (ui != null)
            {
                ui.PostToMainThread(action);
            }
            else
            {
                UnityTaskRunner.PostToMainThread(action);
            }
        }

        private Task<bool> PromptAsync(string text)
        {
            var ui = Ui;
            if (ui != null)
            {
                return ui.PromptAsync(text);
            }

            return Task.FromResult(false);
        }

        private async Task<(Hash hash, TransactionResult txResult, string error)> SendDraftAsync(WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation = true)
        {
            var ui = Ui;
            if (ui != null)
            {
                return await ui.SendTransactionDraftAsync(draft, refreshBalanceAfterConfirmation);
            }

            return (Hash.Null, null, "UI bridge is unavailable.");
        }

        private void ShowTxResult(Hash hash, TransactionResult txResult, string error, string successCustomMessage = null, string failureCustomMessage = null)
        {
            Ui?.TxResultMessage(hash, txResult, error, successCustomMessage, failureCustomMessage);
        }

        private Task<(string[] result, string error)> InvokeScriptOnMainAsync(string chain, byte[] script)
        {
            var tcs = new TaskCompletionSource<(string[] result, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            RunOnUi(() =>
            {
                var ui = Ui;
                if (ui != null)
                {
                    ui.InvokeScriptAsync(chain, script).ContinueWith(task =>
                    {
                        var res = task.Result;
                        tcs.TrySetResult(res);
                    });
                }
                else
                {
                    AccountManager.Instance.InvokeScript(chain, script, (result, error) => tcs.TrySetResult((result, error)));
                }
            });
            return tcs.Task;
        }

        private Task<(bool success, string error)> WriteArchiveOnMainAsync(Hash hash, int blockIndex, byte[] data)
        {
            var tcs = new TaskCompletionSource<(bool success, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            RunOnUi(() =>
            {
                var ui = Ui;
                if (ui != null)
                {
                    ui.WriteArchiveAsync(hash, blockIndex, data).ContinueWith(task => tcs.TrySetResult(task.Result));
                }
                else
                {
                    AccountManager.Instance.WriteArchive(hash, blockIndex, data, (result, error) => tcs.TrySetResult((result, error)));
                }
            });

            return tcs.Task;
        }

        private void PushMessage(string title, string body, MessageKind kind)
        {
            WalletApplicationContext.Instance.Messages.Push(body, title, kind);
        }

        private PlatformKind RequestPlatform(string platform)
        {
            var accountManager = AccountManager.Instance;

            PlatformKind targetPlatform;

            if (!Enum.TryParse<PlatformKind>(platform, true, out targetPlatform))
            {
                return PlatformKind.None;
            }

            if (!accountManager.CurrentAccount.platforms.HasFlag(targetPlatform))
            {
                return PlatformKind.None;
            }

            if (accountManager.CurrentPlatform != targetPlatform)
            {
                accountManager.CurrentPlatform = targetPlatform;
            }

            return targetPlatform;
        }

        private void GetTransactionBySubject(string subject, int id, Action<PhantasmaPhoenix.Protocol.Transaction> callback)
        {
            var script = new ScriptBuilder().CallContract("consensus", "GetTransaction",
                AccountManager.Instance.CurrentAccount.phaAddress, subject).EndScript();

            InvokeScript("main", script, id, (result, error) =>
            {
                if (error != null)
                {
                    callback(null);
                    return;
                }

                var bytes = Base16.Decode(result[0]);
                var tx = PhantasmaPhoenix.Protocol.Transaction.Unserialize(bytes);

                callback(tx);
            });

        }

        private void GetAddressesForTransaction(string subject, int id, Action<Address[]> callback)
        {
            var script = new ScriptBuilder().CallContract("consensus", "GetAddressesForTransaction",
                AccountManager.Instance.CurrentAccount.phaAddress, subject).EndScript();

            InvokeScript("main", script, id, (result, error) =>
            {
                if (error != null)
                {
                    callback(null);
                    return;
                }

                List<Address> addresses = new List<Address>();
                foreach (var item in result)
                {
                    var bytes = Base16.Decode(item);
                    var addr = Serialization.Unserialize<VMObject>(bytes).AsAddress();
                    addresses.Add(addr);
                }

                callback(addresses.ToArray());
            });

        }

        protected override void GetAccount(string platform, int version, Action<Account, string> callback)
        {
            var targetPlatform = RequestPlatform(platform);
            if (targetPlatform == PlatformKind.None)
            {
                callback(new Account(), "Unsupported target platform: " + platform);
                return;
            }

            var accountManager = AccountManager.Instance;

            if (accountManager.CurrentPlatform != targetPlatform)
            {
                accountManager.CurrentPlatform = targetPlatform;
                PushMessage("Phantasma Link", $"Changed current platform to: {targetPlatform}", MessageKind.Default);
            }

            var account = accountManager.CurrentAccount;

            var state = accountManager.CurrentState;

            if (state == null)
            {
                callback(new Account(), "not logged in, devs should implement this case!");
                return;
            }

            IEnumerable<Balance> balances;

            if (version >= 3)
            {
                balances = state.balances.Select(x => new Balance()
                {
                    symbol = x.Symbol,
                    value = x.Available.ToString(),
                    decimals = x.Decimals,
                    ids = x.Ids
                });
            }
            else
            {
                if (state.balances == null)
                {
                    state.balances = new Poltergeist.Balance[0];
                }

                balances = state.balances.Select(x => new Balance()
                {
                    symbol = x.Symbol,
                    value = x.Available.ToString(),
                    decimals = x.Decimals
                });
            }

            var accountExport = new Account()
            {
                name = account.name,
                alias = account.name,
                address = AccountManager.Instance.MainState.address,
                balances = balances.ToArray(),
                avatar = state.avatarData,
                platform = platform,
                external = targetPlatform != PlatformKind.Phantasma ? state.address : ""
            };

            if (version == 3 && targetPlatform == PlatformKind.Neo)
            {
                accountExport.external = account.neoAddressN3;
            }

            callback(accountExport, null);
        }

        protected override void GetPeer(Action<string> callback)
        {
            callback(AccountManager.Instance.Settings.phantasmaRPCURL);
        }

        protected override void GetNexus(Action<string> callback)
        {
            callback(AccountManager.Instance.Settings.nexusName);
        }

        protected override void GetN3Address(Action<string> callback)
        {
            callback(AccountManager.Instance.CurrentAccount.neoAddressN3);
        }

        protected override void GetWalletVersion(Action<string> callback)
        {
            callback(Application.version.StartsWith("v")
                ? Application.version.Substring(1)
                : Application.version);
        }


        protected override void InvokeScript(string chain, byte[] script, int id, Action<string[], string> callback)
        {
            InvokeScriptOnMainAsync(chain, script).ContinueWith(task =>
            {
                var res = task.Result;
                callback(res.result, res.error);
            });
        }

        protected override void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback)
        {
            WriteArchiveOnMainAsync(hash, blockIndex, data).ContinueWith(task =>
            {
                var res = task.Result;
                callback(res.success, res.error);
            });
        }

        protected override void FetchAndMultiSignature(string subject, string platform, SignatureKind kind, int id, Action<bool, string> callback)
        {
            var accountManager = AccountManager.Instance;

            var targetPlatform = RequestPlatform(platform);
            if (targetPlatform == PlatformKind.None || targetPlatform == PlatformKind.Neo)
            {
                callback(false, "Unsupported platform: " + platform);
                return;
            }

            var state = AccountManager.Instance.CurrentState;
            if (state == null)
            {
                callback(false, "not logged in");
                return;
            }

            var account = AccountManager.Instance.CurrentAccount;

            RunOnUi(() =>
            {

                GetTransactionBySubject(subject, id, transaction =>
                {
                    GetAddressesForTransaction(subject, id, addresses =>
                    {
                        if (transaction.Signatures.Length >= addresses.Length)
                        {
                            callback(false, "Transaction already signed by all addresses");
                            return;
                        }

                        if (transaction.Signatures.Length + 1 == addresses.Length)
                        {
                            // Sign and Execute.
                            //SignAndExecuteTransaction();
                            return;
                        }

                        // SignTransaction and Send to Dapp
                        // SignTransactionAndSendSignature();

                        var description = $"{transaction.Hash}\n{transaction.Expiration}\n{Encoding.UTF8.GetString(transaction.Payload)}\n{Encoding.UTF8.GetString(transaction.Script)}";

                        async Task AskForSignatureAsync()
                        {
                            var consent = await PromptAsync($"The dapp wants to sign the following transaction with your {platform} keys. Accept?\n{description}");
                            AppFocus.Instance.EndFocus();

                            if (!consent)
                            {
                                callback(false, "user rejected");
                                return;
                            }

                            PhantasmaPhoenix.Cryptography.Signature signature;

                            var msg = transaction.ToByteArray(false);

                            var wif = account.GetWif(AccountManager.Instance.CurrentPasswordHash);

                            switch (kind)
                            {
                                case SignatureKind.Ed25519:
                                    var phantasmaKeys = PhantasmaKeys.FromWIF(wif);
                                    signature = phantasmaKeys.Sign(msg);
                                    break;

                                case SignatureKind.ECDSA:
                                    var ethKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                                    var signatureBytes = ECDsa.Sign(msg, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                                    signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                                    break;

                                default:
                                    callback(false, kind + " signatures unsupported");
                                    return;
                            }

                            callback(true, "");
                        }

                        AskForSignatureAsync().Forget(ex => Log.WriteWarning(ex.ToString()));

                    });

                });


            });
        }

        protected override void SignTransactionSignature(PhantasmaPhoenix.Protocol.Transaction transaction, string platform, SignatureKind kind, Action<PhantasmaPhoenix.Cryptography.Signature, string> callback)
        {
            var accountManager = AccountManager.Instance;

            var targetPlatform = RequestPlatform(platform);
            if (targetPlatform == PlatformKind.None || targetPlatform == PlatformKind.Neo)
            {
                callback(null, "Unsupported platform: " + platform);
                return;
            }

            var state = AccountManager.Instance.CurrentState;
            if (state == null)
            {
                callback(null, "not logged in");
                return;
            }

            var account = AccountManager.Instance.CurrentAccount;

            RunOnUi(() =>
            {
                var description = $"{transaction.Hash}\n{transaction.Expiration}\n{Encoding.UTF8.GetString(transaction.Payload)}\n{Encoding.UTF8.GetString(transaction.Script)}";

                async Task AskForSignatureAsync()
                {
                    var consent = await PromptAsync($"The dapp wants to sign the following transaction with your {platform} keys. Accept?\n{description}");
                    AppFocus.Instance.EndFocus();

                    if (!consent)
                    {
                        callback(null, "user rejected");
                        return;
                    }

                    PhantasmaPhoenix.Cryptography.Signature signature;
                    var msg = transaction.ToByteArray(false);
                    var wif = account.GetWif(AccountManager.Instance.CurrentPasswordHash);

                    switch (kind)
                    {
                        case SignatureKind.Ed25519:
                            var phantasmaKeys = PhantasmaKeys.FromWIF(wif);
                            signature = phantasmaKeys.Sign(msg);
                            break;

                        case SignatureKind.ECDSA:
                            var ethKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                            var signatureBytes = ECDsa.Sign(msg, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                            signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                            break;

                        default:
                            callback(null, kind + " signatures unsupported");
                            return;
                    }

                    callback(signature, "");
                }

                AskForSignatureAsync().Forget(ex => Log.WriteWarning(ex.ToString()));

            });
        }

        protected override void SignTransaction(string platform, SignatureKind kind, string chain, byte[] script, byte[] payload, int id, ProofOfWork pow, Action<Hash, string> callback)
        {
            var accountManager = AccountManager.Instance;

            if (accountManager.Settings.devMode)
            {
                Log.Write($"WalletConnector: SignTransaction(): Script description: Platform: {platform}\n" +
                    $"SignatureKind: {kind}\n" +
                    $"Chain: {chain}\n" +
                    $"Script: {Base16.Encode(script)}\n" +
                    $"Payload: '{(payload == null ? "" : Encoding.UTF8.GetString(payload))}'\n" +
                    $"ProofOfWork: {pow}");
            }

            var targetPlatform = RequestPlatform(platform);
            if (targetPlatform == PlatformKind.None)
            {
                callback(Hash.Null, "Unsupported platform: " + platform);
                return;
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                callback(Hash.Null, "not logged in");
                return;
            }

            var nexus = accountManager.Settings.nexusName;
            var account = accountManager.CurrentAccount;

            RunOnUi(() =>
            {
                async Task HandleDescriptionAsync()
                {
                    try
                    {
                        var (description, error) = await DescriptionUtils.GetDescriptionAsync(script, accountManager.Settings.devMode, CancellationToken.None);

                        if (description == null)
                        {
                            Log.Write("Error during description parsing.\nDetails: " + error);
                        }
                        else
                        {
                            Log.Write("Script description: " + description);
                        }

                        var consent = await PromptAsync("Allow dapp to send a transaction on your behalf?\n" + description);
                        if (consent)
                        {
                            var draft = WalletTransactionDraft.ForSingleScript(description, script, chain, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, pow, payload);
                            var (hash, _, sendError) = await SendDraftAsync(draft);
                            AppFocus.Instance.EndFocus();
                            callback(hash, sendError);
                        }
                        else
                        {
                            AppFocus.Instance.EndFocus();
                            callback(Hash.Null, "user rejected");
                        }
                    }
                    catch (Exception e)
                    {
                        PushMessage("WalletLink", $"Error during description parsing.\nContact the developers.\nDetails: {e.Message}", MessageKind.Error);
                        callback(Hash.Null, "description parsing error");
                    }
                }

                HandleDescriptionAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
            });
        }

        protected override void SignCarbonTransactionAndBroadcast(byte[] txBytes, Action<Hash, string> callback)
        {
            var accountManager = AccountManager.Instance;

            var state = accountManager.CurrentState;
            if (state == null)
            {
                callback(Hash.Null, "not logged in");
                return;
            }

            var nexus = accountManager.Settings.nexusName;
            var account = accountManager.CurrentAccount;

            RunOnUi(() =>
            {
                async Task HandleDescriptionAsync()
                {
                    try
                    {
                        var txMsg = CarbonBlob.New<TxMsg>(txBytes);
                        var (description, error) = await DescriptionUtils.GetCarbonDescriptionAsync(txMsg, accountManager.Settings.devMode, CancellationToken.None);

                        if (description == null)
                        {
                            Log.Write("Error during description parsing.\nDetails: " + error);
                        }
                        else
                        {
                            Log.Write("Script description: " + description);
                        }

                        var consent = await PromptAsync($"Allow dapp to send a transaction on your behalf?\n\nCurrent nexus: {nexus}, chain: main\n\n{description}");
                        if (consent)
                        {
                            var draft = WalletTransactionDraft.ForCarbon(description, txMsg, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                            var (hash, txResult, sendError) = await SendDraftAsync(draft);
                            AppFocus.Instance.EndFocus();

                            callback(hash, sendError);
                            ShowTxResult(hash, txResult, sendError, $"The transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");
                        }
                        else
                        {
                            AppFocus.Instance.EndFocus();
                            callback(Hash.Null, "user rejected");
                        }
                    }
                    catch (Exception e)
                    {
                        PushMessage("WalletLink", $"Error during description parsing.\nContact the developers.\nDetails: {e.Message}", MessageKind.Error);
                        callback(Hash.Null, "description parsing error");
                    }
                }

                HandleDescriptionAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
            });
        }

        protected override void SignData(string platform, SignatureKind kind, byte[] data, int id, Action<string, string, string> callback)
        {
            var accountManager = AccountManager.Instance;

            var targetPlatform = RequestPlatform(platform);
            if (targetPlatform == PlatformKind.None)
            {
                callback(null, null, "Unsupported platform: " + platform);
                return;
            }

            var state = AccountManager.Instance.CurrentState;
            if (state == null)
            {
                callback(null, null, "not logged in");
                return;
            }

            var account = AccountManager.Instance.CurrentAccount;

            RunOnUi(() =>
            {
                var description = System.Text.Encoding.UTF8.GetString(data);

                async Task AskForDataSignatureAsync()
                {
                    var consent = await PromptAsync($"The dapp wants to sign the following data with your {platform} keys. Accept?\n{description}");
                    AppFocus.Instance.EndFocus();

                    if (!consent)
                    {
                        callback(null, null, "user rejected");
                        return;
                    }

                    var randomValue = UnityEngine.Random.Range(0, int.MaxValue);
                    var randomBytes = BitConverter.GetBytes(randomValue);

                    var msg = ByteArrayUtils.ConcatBytes(randomBytes, data);

                    PhantasmaPhoenix.Cryptography.Signature signature;

                    var wif = account.GetWif(AccountManager.Instance.CurrentPasswordHash);
                    var phantasmaKeys = PhantasmaKeys.FromWIF(wif);

                    switch (kind)
                    {
                        case SignatureKind.Ed25519:
                            signature = phantasmaKeys.Sign(msg);
                            break;

                        case SignatureKind.ECDSA:

                            if (targetPlatform == PlatformKind.Ethereum || targetPlatform == PlatformKind.BSC)
                            {
                                var ethKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);

                                var signatureBytes = ECDsa.Sign(msg, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                                signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                            }
                            else
                            {
                                var neoKeys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
                                var signatureBytes = ECDsa.Sign(msg, neoKeys.PrivateKey, ECDsaCurve.Secp256k1);
                                signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                            }
                            break;

                        default:
                            callback(null, null, kind + " signatures unsupported");
                            return;
                    }

                    byte[] sigBytes = null;

                    using (var stream = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(stream))
                        {
                            writer.WriteSignature(signature);
                        }

                        sigBytes = stream.ToArray();
                    }

                    var hexSig = Base16.Encode(sigBytes);
                    var hexRand = Base16.Encode(randomBytes);

                    callback(hexSig, hexRand, null);
                }

                AskForDataSignatureAsync().Forget(ex => Log.WriteWarning(ex.ToString()));

            });
        }

        protected override void Authorize(string dapp, string token, int version, Action<bool, string> callback)
        {
            var accountManager = AccountManager.Instance;

            if (version > WalletConnector.LinkProtocol)
            {
                callback(false, "unknown Phantasma Link version " + version);
                return;
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
                accountManager.CurrentPlatform = PlatformKind.Phantasma;

            var state = AccountManager.Instance.CurrentState;
            if (state == null)
            {
                callback(false, "not logged in");
                return;
            }

            RunOnUi(() =>
            {
                async Task AskAuthorizationAsync()
                {
                    var result = await PromptAsync($"Give access to dApp \"{dapp}\" to your \"{state.name}\" account?");
                    AppFocus.Instance.EndFocus();

                    if (result)
                    {
                        state.RegisterDappToken(dapp, token);
                    }

                    callback(result, result ? null : "rejected");
                }

                AskAuthorizationAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
            });

        }

        #region IWalletLinkV5Ops
        // The clean v5 surface. Each method reuses the SAME internal consent/account/sign/broadcast
        // logic the legacy dispatcher uses (the legacy protected methods above), and translates the
        // legacy-shaped (value, errorString) results into the structured v5 contract. The legacy
        // error strings are this wallet's own internal detail and are mapped here, so nothing
        // legacy ever reaches the v5 protocol (WalletLinkV5). Explicit interface implementation so
        // the v5 surface never collides with the base protected methods of the same name.

        WalletStatus IWalletLinkV5Ops.Status => Status;

        void IWalletLinkV5Ops.Connect(string dappName, string sessionToken, Action<LinkConnectResult> done)
        {
            // The internal ops still speak the legacy contract version (they guard on it and shape
            // the account export by it); the v5 protocol version was already validated upstream.
            Authorize(dappName, sessionToken, LinkProtocol, (authorized, authError) =>
            {
                if (!authorized)
                {
                    done(LinkConnectResult.Fail(MapWalletFailure(authError), authError));
                    return;
                }

                GetAccount("phantasma", LinkProtocol, (account, accountError) =>
                {
                    if (accountError != null)
                    {
                        done(LinkConnectResult.Fail(MapWalletFailure(accountError), accountError));
                        return;
                    }

                    GetWalletVersion(version =>
                        done(LinkConnectResult.Ok(ToLinkAccount(account), Name, version, Nexus)));
                });
            });
        }

        void IWalletLinkV5Ops.GetAccount(Action<LinkAccountResult> done)
        {
            GetAccount("phantasma", LinkProtocol, (account, error) =>
            {
                if (error != null)
                {
                    done(LinkAccountResult.Fail(MapWalletFailure(error), error));
                    return;
                }
                done(LinkAccountResult.Ok(ToLinkAccount(account)));
            });
        }

        void IWalletLinkV5Ops.GetChains(Action<LinkChains> done)
            => GetNexus(nexus => done(new LinkChains { Nexus = nexus }));

        void IWalletLinkV5Ops.GetWalletInfo(Action<LinkWalletInfo> done)
            => GetWalletVersion(version =>
                GetPeer(rpc => done(new LinkWalletInfo { Name = Name, Version = version, Rpc = rpc })));

        void IWalletLinkV5Ops.SendTransaction(byte[] serializedTx, LinkTxFormat format, SignatureKind kind, ProofOfWork pow, Action<LinkSendResult> done)
        {
            switch (format)
            {
                case LinkTxFormat.Carbon:
                    // Reuse the existing Carbon broadcast path verbatim (no duplicated logic).
                    SignCarbonTransactionAndBroadcast(serializedTx, (hash, error) =>
                    {
                        if (hash == Hash.Null)
                        {
                            done(LinkSendResult.Fail(MapWalletFailure(error), error));
                            return;
                        }
                        done(LinkSendResult.Ok(hash));
                    });
                    break;

                default:
                    // "script" format is a follow-up; not advertised in the capability handshake.
                    done(LinkSendResult.Fail(LinkFailure.InvalidTransaction, "Transaction format 'script' is not yet supported by this wallet"));
                    break;
            }
        }

        void IWalletLinkV5Ops.ConfirmPairing(LinkPairingParams pairing, Action<bool> done)
        {
            RunOnUi(() =>
            {
                async Task AskPairingAsync()
                {
                    var name = string.IsNullOrEmpty(pairing.DappName) ? pairing.Topic : pairing.DappName;
                    var consent = await PromptAsync($"Pair with dApp \"{name}\"?\n\nIt will be able to send requests to this wallet via deep links.");
                    AppFocus.Instance.EndFocus();
                    done(consent);
                }

                AskPairingAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
            });
        }

        void IWalletLinkV5Ops.InvokeScript(string chain, byte[] script, Action<LinkInvokeResult> done)
        {
            InvokeScript(chain, script, 0, (results, error) =>
            {
                if (results == null)
                {
                    done(LinkInvokeResult.Fail(MapWalletFailure(error), error));
                    return;
                }
                done(LinkInvokeResult.Ok(results));
            });
        }

        // Map this wallet's own internal error strings (from the legacy protected ops) to a
        // structured v5 failure. Confined to the wallet; the v5 protocol never sees these strings.
        private static LinkFailure MapWalletFailure(string error)
        {
            if (string.IsNullOrEmpty(error)) return LinkFailure.Internal;
            if (error == "rejected" || error == "user rejected") return LinkFailure.UserRejected;
            if (error == "not logged in") return LinkFailure.NotLoggedIn;
            if (error == "description parsing error") return LinkFailure.InvalidTransaction;
            return LinkFailure.Internal;
        }

        private static LinkAccount ToLinkAccount(Account account)
        {
            var balances = account.balances ?? Array.Empty<Balance>();
            return new LinkAccount
            {
                Address = account.address ?? "",
                Name = account.name ?? "",
                Avatar = account.avatar ?? "",
                Balances = balances.Select(b => new LinkBalance
                {
                    Symbol = b.symbol ?? "",
                    Value = b.value ?? "",
                    Decimals = (int)b.decimals,
                    Ids = b.ids ?? Array.Empty<string>(),
                }).ToArray(),
            };
        }
        #endregion
    }
}
