using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LunarLabs.Parser;
using Phantasma.Core.Numerics;
using Phantasma.SDK;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.VM;
using Poltergeist.Neo2.Core;
using Poltergeist.PhantasmaLegacy.Ethereum;
using UnityEngine.Device;

namespace Poltergeist
{
    public class WalletConnector : WalletLink
    {
        public override string Nexus => AccountManager.Instance.Settings.nexusName;
        public override string Name => "Poltergeist Lite";

        protected override WalletStatus Status => AccountManager.Instance.CurrentState != null ? WalletStatus.Ready : WalletStatus.Closed;

        public WalletConnector() : base()
        {
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
                foreach ( var item in result)
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
                WalletGUI.Instance.MessageBox(MessageKind.Default, "Phantasma Link changed current platform to :" + targetPlatform);
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
                    value = UnitConversion.ToBigInteger(x.Available, x.Decimals).ToString(),
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
                    value = UnitConversion.ToBigInteger(x.Available, x.Decimals).ToString(),
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
            
            if ( version == 3 && targetPlatform == PlatformKind.Neo)
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
            WalletGUI.Instance.CallOnUIThread(() =>
            {
                try
                {
                    WalletGUI.Instance.InvokeScript(chain, script, (results, msg) =>
                    {
                        callback(results, msg);
                    });
                }
                catch (Exception e)
                {
                    callback(null, "InvokeScript call error: " + e.Message);
                    return;
                }
            });
        }

        protected override void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback)
        {
            WalletGUI.Instance.CallOnUIThread(() =>
            {
                try
                {
                    WalletGUI.Instance.WriteArchive(hash, blockIndex, data, (result, msg) =>
                    {
                        callback(result, msg);
                    });
                }
                catch (Exception e)
                {
                    callback(false, "WriteArchive call error: " + e.Message);
                    return;
                }
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
            
            WalletGUI.Instance.CallOnUIThread(() =>
            {
                
                GetTransactionBySubject(subject, id, transaction =>
                {
                    GetAddressesForTransaction(subject, id, addresses =>
                    {
                        if (  transaction.Signatures.Length >= addresses.Length)
                        {
                            callback(false, "Transaction already signed by all addresses");
                            return;
                        }
                        
                        if ( transaction.Signatures.Length + 1 == addresses.Length)
                        {
                            // Sign and Execute.
                            //SignAndExecuteTransaction();
                            return;
                        }
                        
                        // SignTransaction and Send to Dapp
                        // SignTransactionAndSendSignature();
                        
                        var description = $"{transaction.Hash}\n{transaction.Expiration}\n{Encoding.UTF8.GetString(transaction.Payload)}\n{Encoding.UTF8.GetString(transaction.Script)}";

                        WalletGUI.Instance.Prompt($"The dapp wants to sign the following transaction with your {platform} keys. Accept?\n{description}", (success) =>
                        {
                            AppFocus.Instance.EndFocus();

                            if (success)
                            {
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
                                        var ethKeys = EthereumKey.FromWIF(wif);
                                        var signatureBytes = ECDsa.Sign(msg, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                                        signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                                        break;

                                    default:
                                        callback(false, kind + " signatures unsupported");
                                        return;
                                }
                                
                                // Send to dapp the signature and the addresses that were used to sign
                                

                                callback(true, "");
                            }
                            else
                            {
                                callback(false, "user rejected");
                            }
                        });

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

            WalletGUI.Instance.CallOnUIThread(() =>
            {
                var description = $"{transaction.Hash}\n{transaction.Expiration}\n{Encoding.UTF8.GetString(transaction.Payload)}\n{Encoding.UTF8.GetString(transaction.Script)}";

                WalletGUI.Instance.Prompt($"The dapp wants to sign the following transaction with your {platform} keys. Accept?\n{description}", (success) =>
                {
                    AppFocus.Instance.EndFocus();

                    if (success)
                    {
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
                                var ethKeys = EthereumKey.FromWIF(wif);
                                var signatureBytes = ECDsa.Sign(msg, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                                signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                                break;

                            default:
                                callback(null, kind + " signatures unsupported");
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

                        callback(signature, "");
                    }
                    else
                    {
                        callback(null, "user rejected");
                    }
                });

            });
        }

        protected override void SignTransaction(string platform, SignatureKind kind, string chain, byte[] script, byte[] payload, int id, ProofOfWork pow, Action<Hash, string> callback)
        {
            var accountManager = AccountManager.Instance;

            if(accountManager.Settings.devMode)
            {
                Log.Write($"WalletConnector: SignTransaction(): Script description: Platform: {platform}\n"+
                    $"SignatureKind: {kind}\n"+
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

            WalletGUI.Instance.CallOnUIThread(() =>
            {
                try
                {
                    WalletGUI.Instance.StartCoroutine(DescriptionUtils.GetDescription(script, accountManager.Settings.devMode, (description, error) => {

                        if (description == null)
                        {
                            Log.Write("Error during description parsing.\nDetails: " + error);
                            //description = "Could not decode transaction contents. (Not an error)";
                        }
                        else
                        {
                            Log.Write("Script description: " + description);
                        }

                        WalletGUI.Instance.Prompt("Allow dapp to send a transaction on your behalf?\n" + description, (success) =>
                        {
                            if (success)
                            {
                                WalletGUI.Instance.SendTransaction(description, script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, payload, chain, pow, (hash, txResult, error) =>
                                {
                                    AppFocus.Instance.EndFocus();

                                    callback(hash, error);
                                });
                            }
                            else
                            {
                                AppFocus.Instance.EndFocus();
                                callback(Hash.Null, "user rejected");
                            }
                        });
                    }));
                }
                catch( Exception e )
                {
                    WalletGUI.Instance.MessageBox(MessageKind.Error, "Error during description parsing.\nContact the developers.\nDetails: " + e.Message);
                    callback(Hash.Null, "description parsing error");
                    return;
                }
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

            WalletGUI.Instance.CallOnUIThread(() =>
            {
                var description = System.Text.Encoding.UTF8.GetString(data);

                WalletGUI.Instance.Prompt($"The dapp wants to sign the following data with your {platform} keys. Accept?\n{description}", (success) =>
                {
                    AppFocus.Instance.EndFocus();

                    if (success)
                    {
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

                                if ( targetPlatform == PlatformKind.Ethereum || targetPlatform == PlatformKind.BSC)
                                {
                                    var ethKeys = EthereumKey.FromWIF(wif);
                                
                                    var signatureBytes = ECDsa.Sign(msg, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                                    signature = new ECDsaSignature(signatureBytes, ECDsaCurve.Secp256k1);
                                }
                                else
                                {
                                    var neoKeys = NeoKeys.FromWIF(wif);
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
                    else
                    {
                        callback(null, null, "user rejected");
                    }
                });

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

            WalletGUI.Instance.CallOnUIThread(() =>
            {
                WalletGUI.Instance.Prompt($"Give access to dApp \"{dapp}\" to your \"{state.name}\" account?", (result) =>
               {
                   AppFocus.Instance.EndFocus();

                   if (result)
                   {
                       state.RegisterDappToken(dapp, token);
                   }

                   callback(result,  result ? null :"rejected");
               });
           });

        }
    }
}