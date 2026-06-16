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
using PhantasmaPhoenix.Unity.Core;
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
            // RequestPlatform validates the platform and, as a side effect, already makes it the
            // wallet's current platform; None means it is unparsable or not enabled on this account.
            var targetPlatform = RequestPlatform(platform);
            if (targetPlatform == PlatformKind.None)
            {
                callback(new Account(), "Unsupported target platform: " + platform);
                return;
            }

            var state = AccountManager.Instance.CurrentState;
            if (state == null)
            {
                callback(new Account(), "not logged in");
                return;
            }

            var account = AccountManager.Instance.CurrentAccount;

            // Balances stay null until the first refresh; normalize to empty so the projection
            // below is null-safe on every protocol version (previously only the legacy branch
            // guarded this, so a v3+ query on a freshly opened account could throw).
            if (state.balances == null)
            {
                state.balances = Array.Empty<Poltergeist.Balance>();
            }

            // Protocol 3 added per-token NFT ids; older dapps do not expect that field, so emit it
            // only from v3 on and otherwise keep the original scalar-only balance shape.
            var includeIds = version >= 3;
            var balances = state.balances.Select(token =>
            {
                var balance = new Balance()
                {
                    symbol = token.Symbol,
                    value = token.Available.ToString(),
                    decimals = token.Decimals
                };

                if (includeIds)
                {
                    balance.ids = token.Ids;
                }

                return balance;
            }).ToArray();

            var accountExport = new Account()
            {
                name = account.name,
                alias = account.name,
                address = AccountManager.Instance.MainState.address,
                balances = balances,
                avatar = state.avatarData,
                platform = platform,
                external = targetPlatform != PlatformKind.Phantasma ? state.address : ""
            };

            // Protocol 3 reports the Neo identity from the dedicated N3 address field.
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
                            WindowActivator.Instance.Restore();

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
                    WindowActivator.Instance.Restore();

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
                            WindowActivator.Instance.Restore();
                            callback(hash, sendError);
                        }
                        else
                        {
                            WindowActivator.Instance.Restore();
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
                            WindowActivator.Instance.Restore();

                            callback(hash, sendError);
                            ShowTxResult(hash, txResult, sendError, $"The transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");
                        }
                        else
                        {
                            WindowActivator.Instance.Restore();
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
                    WindowActivator.Instance.Restore();

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
            // Legacy connect: once the user consents, record the token in the per-account dappTokens
            // map (the legacy revocation ledger); the base HandleAuthorize then mirrors it into
            // _connections. v5 connect shares ONLY the consent prompt below - it never writes
            // dappTokens, because a v5 dapp lives in the v5 session store and a stray dappTokens
            // entry would be one that legacy Revoke cannot resolve (the original logout crash).
            RequestDappConsent(dapp, version, (granted, error) =>
            {
                if (granted)
                {
                    AccountManager.Instance.CurrentState?.RegisterDappToken(dapp, token);
                }
                callback(granted, error);
            });
        }

        // Shared, version-checked dApp consent prompt ("give dApp X access to account Y?"). This is
        // the ONLY piece the legacy and v5 connect paths share. It deliberately knows nothing about
        // session tokens or the legacy dappTokens ledger: the caller decides what to persist on a
        // grant (legacy writes dappTokens; v5 lets the dispatcher own the session), so no legacy
        // concept leaks into the clean v5 surface.
        private void RequestDappConsent(string dapp, int version, Action<bool, string> callback)
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
                    WindowActivator.Instance.Restore();
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

        void IWalletLinkV5Ops.Connect(string dappName, Action<LinkConnectResult> done)
        {
            // Reuse ONLY the shared consent prompt; the v5 session id is owned by the dispatcher, so
            // this path writes NO legacy dappTokens. The internal consent still speaks the legacy
            // contract version (it guards on it); the v5 protocol version was validated upstream.
            RequestDappConsent(dappName, LinkProtocol, (authorized, authError) =>
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

                case LinkTxFormat.Script:
                    SignPrebuiltScriptTransaction(serializedTx, kind, broadcast: true, (signedTx, hash, error) =>
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
                    done(LinkSendResult.Fail(LinkFailure.InvalidTransaction, $"Transaction format '{format}' is not supported by this wallet"));
                    break;
            }
        }

        void IWalletLinkV5Ops.SignTransaction(byte[] serializedTx, LinkTxFormat format, SignatureKind kind, ProofOfWork pow, Action<LinkSignTransactionResult> done)
        {
            switch (format)
            {
                case LinkTxFormat.Carbon:
                    // Carbon witnesses are Ed25519-only in the current chain; refuse other kinds
                    // with the structured failure instead of silently signing with the wrong key.
                    if (kind != SignatureKind.Ed25519)
                    {
                        done(LinkSignTransactionResult.Fail(LinkFailure.UnsupportedSignatureKind, "signature kind unsupported"));
                        return;
                    }
                    SignCarbonTransactionOnly(serializedTx, (signedTx, error) =>
                    {
                        if (signedTx == null)
                        {
                            done(LinkSignTransactionResult.Fail(MapWalletFailure(error), error));
                            return;
                        }
                        done(LinkSignTransactionResult.Ok(signedTx));
                    });
                    break;

                case LinkTxFormat.Script:
                    SignPrebuiltScriptTransaction(serializedTx, kind, broadcast: false, (signedTx, _, error) =>
                    {
                        if (signedTx == null)
                        {
                            done(LinkSignTransactionResult.Fail(MapWalletFailure(error), error));
                            return;
                        }
                        done(LinkSignTransactionResult.Ok(signedTx));
                    });
                    break;

                default:
                    done(LinkSignTransactionResult.Fail(LinkFailure.InvalidTransaction, $"Transaction format '{format}' is not supported by this wallet"));
                    break;
            }
        }

        void IWalletLinkV5Ops.SignMessage(byte[] message, string display, Action<LinkSignMessageResult> done)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;
            if (state == null)
            {
                done(LinkSignMessageResult.Fail(LinkFailure.NotLoggedIn, "not logged in"));
                return;
            }
            var account = accountManager.CurrentAccount;

            RunOnUi(() =>
            {
                async Task AskSignMessageAsync()
                {
                    // Spec §8 display rule: show the message as UTF-8 when it decodes cleanly,
                    // otherwise a digest + byte length; the dApp's display hint comes first.
                    var preview = DescribeMessageForConsent(message);
                    var shown = string.IsNullOrEmpty(display) ? preview : display + "\n\n" + preview;
                    var consent = await PromptAsync($"The dApp asks you to sign a message. This is NOT a transaction and cannot move funds.\n\n{shown}");
                    WindowActivator.Instance.Restore();

                    if (!consent)
                    {
                        done(LinkSignMessageResult.Fail(LinkFailure.UserRejected, "user rejected"));
                        return;
                    }

                    // §8 construction: DOMAIN_TAG || random(32, CSPRNG) || message - the domain
                    // tag makes the signature non-replayable as a transaction, the random comes
                    // from a cryptographic RNG (the legacy SignData used 4 weak UnityRandom
                    // bytes, which must never be reused). The signature is a RAW 64-byte
                    // Ed25519 detached signature so any NaCl stack can verify it against the
                    // account public key.
                    var random = LinkSignMessage.GenerateRandom();
                    var payload = LinkSignMessage.BuildPayload(message, random);
                    var wif = account.GetWif(accountManager.CurrentPasswordHash);
                    var keys = PhantasmaKeys.FromWIF(wif);
                    var signature = Ed25519.Sign(payload, keys.PrivateKey);
                    done(LinkSignMessageResult.Ok(signature, random));
                }

                AskSignMessageAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
            });
        }

        /// <summary>Human preview of a to-be-signed message: clean UTF-8 verbatim, anything
        /// binary as a digest + length (the user must never confirm invisible content).</summary>
        private static string DescribeMessageForConsent(byte[] message)
        {
            var text = Encoding.UTF8.GetString(message);
            var looksReadable = !text.Contains('\uFFFD') &&
                text.All(ch => !char.IsControl(ch) || ch == '\n' || ch == '\r' || ch == '\t');
            if (looksReadable && text.Length > 0)
            {
                return text;
            }
            return $"(binary message, {message.Length} bytes, SHA-256 {Base16.Encode(message.Sha256())})";
        }

        /// <summary>Carbon sign-only: parse, describe, ask consent, sign - but never broadcast
        /// (the dApp submits the returned SignedTxMsg itself).</summary>
        private void SignCarbonTransactionOnly(byte[] txBytes, Action<byte[], string> callback)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;
            if (state == null)
            {
                callback(null, "not logged in");
                return;
            }
            var nexus = accountManager.Settings.nexusName;
            var account = accountManager.CurrentAccount;

            RunOnUi(() =>
            {
                async Task HandleAsync()
                {
                    try
                    {
                        var txMsg = CarbonBlob.New<TxMsg>(txBytes);
                        var (description, error) = await DescriptionUtils.GetCarbonDescriptionAsync(txMsg, accountManager.Settings.devMode, CancellationToken.None);
                        if (description == null)
                        {
                            Log.Write("Error during description parsing.\nDetails: " + error);
                        }

                        var consent = await PromptAsync($"Allow dapp to SIGN a transaction WITHOUT sending it? The dapp will submit it itself.\n\nCurrent nexus: {nexus}, chain: main\n\n{description}");
                        WindowActivator.Instance.Restore();
                        if (!consent)
                        {
                            callback(null, "user rejected");
                            return;
                        }

                        var wif = account.GetWif(accountManager.CurrentPasswordHash);
                        var keys = PhantasmaKeys.FromWIF(wif);
                        callback(TxMsgSigner.Sign(txMsg, keys), "");
                    }
                    catch (Exception e)
                    {
                        PushMessage("WalletLink", $"Error during description parsing.\nContact the developers.\nDetails: {e.Message}", MessageKind.Error);
                        callback(null, "description parsing error");
                    }
                }

                HandleAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
            });
        }

        /// <summary>
        /// Shared v5 path for PREBUILT classic (script-format) transactions: parse, describe the
        /// embedded script, ask consent, sign with the REQUESTED key kind (the legacy broadcast
        /// path ignored the kind - v5 fixes that), then either return the signed bytes or
        /// broadcast them via sendRawTransaction. `pow` from the request is intentionally NOT
        /// applied here: mining mutates the payload of a transaction the dApp already built
        /// (and the SDK's Transaction.Mine is currently disabled); proof-of-work on a prebuilt
        /// tx is the BUILDER's responsibility.
        /// </summary>
        private void SignPrebuiltScriptTransaction(byte[] serializedTx, SignatureKind kind, bool broadcast, Action<byte[], Hash, string> callback)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;
            if (state == null)
            {
                callback(null, Hash.Null, "not logged in");
                return;
            }

            var tx = PhantasmaPhoenix.Protocol.Transaction.Unserialize(serializedTx);
            if (tx == null)
            {
                callback(null, Hash.Null, "invalid transaction");
                return;
            }

            var nexus = accountManager.Settings.nexusName;
            var account = accountManager.CurrentAccount;

            RunOnUi(() =>
            {
                async Task HandleAsync()
                {
                    try
                    {
                        var (description, error) = await DescriptionUtils.GetDescriptionAsync(tx.Script, accountManager.Settings.devMode, CancellationToken.None);
                        if (description == null)
                        {
                            Log.Write("Error during description parsing.\nDetails: " + error);
                        }

                        var action = broadcast ? "send a transaction on your behalf" : "SIGN a transaction WITHOUT sending it (the dapp will submit it itself)";
                        var consent = await PromptAsync($"Allow dapp to {action}?\n\nTransaction nexus: {tx.NexusName}, chain: {tx.ChainName} (wallet nexus: {nexus})\n\n{description}");
                        if (!consent)
                        {
                            WindowActivator.Instance.Restore();
                            callback(null, Hash.Null, "user rejected");
                            return;
                        }

                        // Wallet-side broadcast (Ed25519) goes through the wallet's native send
                        // flow - the explicit Send dialog plus on-chain confirmation, like the
                        // carbon and legacy send paths - instead of a silent direct broadcast.
                        // Sign-only and the rare non-Ed25519 broadcast keep the inline path below.
                        if (broadcast && kind == SignatureKind.Ed25519)
                        {
                            var sendUi = Ui;
                            if (sendUi == null)
                            {
                                WindowActivator.Instance.Restore();
                                callback(null, Hash.Null, "UI bridge is unavailable.");
                                return;
                            }

                            var draft = WalletTransactionDraft.ForSingleScript(description, tx.Script, tx.ChainName,
                                accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None, tx.Payload);
                            var (routedHash, _, routedError) = await sendUi.SendTransactionDraftAsync(draft);
                            WindowActivator.Instance.Restore();
                            if (routedHash == Hash.Null && string.IsNullOrEmpty(routedError))
                            {
                                routedError = "transaction was not sent";
                            }
                            callback(null, routedHash, routedError);
                            return;
                        }

                        WindowActivator.Instance.Restore();
                        var msg = tx.ToByteArray(false);
                        var wif = account.GetWif(accountManager.CurrentPasswordHash);
                        PhantasmaPhoenix.Cryptography.Signature signature;
                        switch (kind)
                        {
                            case SignatureKind.Ed25519:
                                signature = PhantasmaKeys.FromWIF(wif).Sign(msg);
                                break;

                            case SignatureKind.ECDSA:
                                var ethKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                                var signatureBytes = ECDsa.Sign(msg, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                                signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                                break;

                            default:
                                callback(null, Hash.Null, "signature kind unsupported");
                                return;
                        }
                        tx.AddSignature(signature);
                        var signedTx = tx.ToByteArray(true);

                        if (!broadcast)
                        {
                            callback(signedTx, Hash.Null, "");
                            return;
                        }

                        // Broadcast via the house async adapter over the coroutine API (the
                        // same AsyncPhantasma.FromApi pattern every modern RPC call uses);
                        // request failures surface as PhantasmaRequestException below.
                        var (sentHash, _, _) = await AsyncPhantasma.FromApi(
                            (Action<string, string, Hash> onSuccess, Action<EPHANTASMA_SDK_ERROR_TYPE, string> onError) =>
                                accountManager.phantasmaApi.SendRawTransaction(
                                    Base16.Encode(signedTx), tx.Hash, onSuccess, onError),
                            CancellationToken.None);
                        if (string.IsNullOrEmpty(sentHash))
                        {
                            callback(null, Hash.Null, "transaction was not broadcast");
                            return;
                        }
                        callback(signedTx, Hash.Parse(sentHash), "");
                    }
                    catch (PhantasmaRequestException e)
                    {
                        // The tx was signed but the node refused/failed the broadcast; this is
                        // an RPC failure, not a malformed transaction.
                        PushMessage("WalletLink", $"Transaction broadcast failed.\nDetails: {e.Message}", MessageKind.Error);
                        callback(null, Hash.Null, "transaction was not broadcast");
                    }
                    catch (Exception e)
                    {
                        PushMessage("WalletLink", $"Error during transaction handling.\nContact the developers.\nDetails: {e.Message}", MessageKind.Error);
                        callback(null, Hash.Null, "description parsing error");
                    }
                }

                HandleAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
            });
        }

        void IWalletLinkV5Ops.ConfirmPairing(LinkPairingParams pairing, Action<bool> done)
        {
            RunOnUi(() =>
            {
                async Task AskPairingAsync()
                {
                    // This approval is the ONLY consent on the one-tap path: right after it the
                    // deeplink endpoint pushes the connect result to the dApp (spec §15 step 3),
                    // so the text must state the full grant - account visibility + requests.
                    // Transactions still get their own confirmation, and the endpoint only
                    // pushes a session when the pairing meta carries a dApp name.
                    var name = string.IsNullOrEmpty(pairing.DappName) ? pairing.Topic : pairing.DappName;
                    var consent = await PromptAsync($"Pair with dApp \"{name}\"?\n\nIt will see your account (address and balances) and will be able to send requests to this wallet via deep links. Transactions will still require your confirmation.");
                    WindowActivator.Instance.Restore();
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
            if (error == "invalid transaction") return LinkFailure.InvalidTransaction;
            if (error == "signature kind unsupported") return LinkFailure.UnsupportedSignatureKind;
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
