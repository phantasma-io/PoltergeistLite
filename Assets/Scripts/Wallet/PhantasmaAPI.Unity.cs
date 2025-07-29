using System;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.Cryptography.Extensions;
using PhantasmaPhoenix.RPC.Types;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace PhantasmaIntegration
{
    public interface IRom
    {
        bool IsEmpty();
        (bool, string) HasParsingError();
        string GetName();
        string GetDescription();
        DateTime GetDate();
    }
    public class CrownRom : IRom
    {
        private bool isEmpty;
        private string parsingError;
        public string tokenId;
        public Address staker;
        public Timestamp date;

        public CrownRom(byte[] rom, string tokenId)
        {
            this.tokenId = tokenId;

            if (rom == null || rom.Length == 0)
            {
                isEmpty = true;
                return;
            }

            try
            {
                using (var stream = new System.IO.MemoryStream(rom))
                {
                    using (var reader = new System.IO.BinaryReader(stream))
                    {
                        UnserializeData(reader);
                    }
                }
            }
            catch (Exception e)
            {
                parsingError = $"Cannot parse ROM '{System.Text.Encoding.ASCII.GetString(rom)}/{BitConverter.ToString(rom)}': {e.Message}";
            }
        }

        public bool IsEmpty() => isEmpty;
        public (bool, string) HasParsingError() => (!string.IsNullOrEmpty(parsingError), parsingError);

        public string GetName() => "CROWN #" + tokenId;
        public string GetDescription() => "";
        public DateTime GetDate() => date;

        private void UnserializeData(System.IO.BinaryReader reader)
        {
            this.staker = reader.ReadAddress();
            this.date = new Timestamp(reader.ReadUInt32());
        }
    }
    public class CustomRom : IRom
    {
        private bool isEmpty;
        private string parsingError;
        private Dictionary<VMObject, VMObject> fields = new Dictionary<VMObject, VMObject>();

        public CustomRom(byte[] romBytes)
        {
            if (romBytes == null || romBytes.Length == 0)
            {
                isEmpty = true;
                return;
            }

            try
            {
                var rom = VMObject.FromBytes(romBytes);
                if (rom.Type == VMType.Struct)
                {
                    fields = (Dictionary<VMObject, VMObject>)rom.Data;
                }
                else
                {
                    parsingError = $"Cannot parse ROM '{System.Text.Encoding.ASCII.GetString(romBytes)}/{BitConverter.ToString(romBytes)}': Unsupported ROM type '{rom.Type}'";
                }
            }
            catch (Exception e)
            {
                parsingError = $"Cannot parse ROM '{System.Text.Encoding.ASCII.GetString(romBytes)}/{BitConverter.ToString(romBytes)}': {e.Message}";
            }
        }

        public bool IsEmpty() => isEmpty;
        public (bool, string) HasParsingError() => (!string.IsNullOrEmpty(parsingError), parsingError);

        public string GetName()
        {
            if (fields.TryGetValue(VMObject.FromObject("name"), out var value))
            {
                return value.AsString();
            }
            return "";
        }
        public string GetDescription()
        {
            if (fields.TryGetValue(VMObject.FromObject("description"), out var value))
            {
                return value.AsString();
            }
            return "";
        }
        public DateTime GetDate()
        {
            if (fields.TryGetValue(VMObject.FromObject("created"), out var value))
            {
                return value.AsTimestamp();
            }
            return new DateTime();
        }
    }

    public static class TokenDataExtensions
    {
        public static IRom ParseRom(this TokenDataResult tokenData, string symbol)
        {
            switch (symbol)
            {
                case "CROWN":
                    return new CrownRom(Base16.Decode(tokenData.Rom), tokenData.Id);
                default:
                    return new CustomRom(Base16.Decode(tokenData.Rom));
            }
        }

        public static string GetPropertyValue(this TokenDataResult tokenData, string key)
        {
            if (tokenData.Properties != null)
            {
                return tokenData.Properties.Where(x => x.Key.ToUpperInvariant() == key.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();
            }

            return null;
        }
    }

    public class PhantasmaAPI
    {
        public readonly string Host;

        public PhantasmaAPI(string host)
        {
            this.Host = host;
        }


        //Returns the account name and balance of given address.
        public IEnumerator GetAccount(string addressText, Action<AccountResult> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<AccountResult>(Host, "getAccount", WebClient.DefaultTimeout, 0, errorHandlingCallback, (account) =>
            {
                callback(account);
            }, addressText);
        }

        public IEnumerator GetContract(string contractName, Action<ContractResult> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<ContractResult>(Host, "getContract", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, DomainSettings.RootChainName, contractName);
        }


        //Returns the address that owns a given name.
        public IEnumerator LookUpName(string name, Action<string> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<string>(Host, "lookUpName", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, name);
        }

        //Returns last X transactions of given address.
        //This api call is paginated, multiple calls might be required to obtain a complete result 
        public IEnumerator GetAddressTransactions(string addressText, uint page, uint pageSize, Action<AccountTransactionsResult, uint, uint> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<PaginatedResult<AccountTransactionsResult>>(Host, "getAddressTransactions", WebClient.DefaultTimeout, 0, errorHandlingCallback, (paginatedResult) =>
            {
                var currentPage = paginatedResult.Page;
                var totalPages = paginatedResult.TotalPages;
                callback(paginatedResult.Result, currentPage, totalPages);
            }, addressText, page, pageSize);
        }

        //Allows to broadcast a signed operation on the network, but it&apos;s required to build it manually.
        public IEnumerator SendRawTransaction(string txData, Hash txHash, Action<string, string, Hash> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<string>(Host, "sendRawTransaction", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result, txData, txHash);
            }, txData);
        }


        //Allows to invoke script based on network state, without state changes.
        public IEnumerator InvokeRawScript(string chainInput, string scriptData, Action<ScriptResult> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<ScriptResult>(Host, "invokeRawScript", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, chainInput, scriptData);
        }


        //Returns information about a transaction by hash.
        public IEnumerator GetTransaction(string hashText, Action<TransactionResult> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<TransactionResult>(Host, "getTransaction", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, hashText);
        }


        //Returns an array of tokens deployed in Phantasma.
        public IEnumerator GetTokens(Action<TokenResult[]> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<TokenResult[]>(Host, "getTokens", 10, 5, errorHandlingCallback, (result) =>
            {
                callback(result);
            });
        }

        private int tokensLoadedSimultaneously = 0;

        //Returns data of a non-fungible token, in hexadecimal format.
        public IEnumerator GetNFT(string symbol, string IDtext, Action<TokenDataResult> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            while (tokensLoadedSimultaneously > 5)
            {
                yield return null;
            }
            tokensLoadedSimultaneously++;

            yield return WebClient.RPCRequest<TokenDataResult>(Host, "getNFT", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                // TODO remove later
                if (string.IsNullOrEmpty(result.Id))
                {
                    result.Id = IDtext;
                }

                callback(result);
            }, symbol, IDtext, true);

            tokensLoadedSimultaneously--;
        }

        //Writes the contents of an incomplete archive.
        public IEnumerator WriteArchive(string hashText, int blockIndex, byte[] blockContent, Action<Boolean> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<string>(Host, "writeArchive", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(Boolean.Parse(result));
            }, hashText, blockIndex, Convert.ToBase64String(blockContent));
        }

        public IEnumerator SignAndSendTransactionWithPayload(PhantasmaKeys keys, IKeyPair otherKeys, string nexus, byte[] script, string chain, BigInteger gasPrice, BigInteger gasLimit, byte[] payload, ProofOfWork PoW, Action<string, string, Hash> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
        {
            Log.Write("Sending transaction... script size: " + script.Length);

            var tx = new PhantasmaPhoenix.Protocol.Transaction(nexus, chain, script, DateTime.UtcNow + TimeSpan.FromMinutes(20), payload);

            /*if (PoW != ProofOfWork.None)
            {
                tx.Mine(PoW);
            }*/

            Hash txHash = tx.SignEx(keys, null);
            if (otherKeys != null)
            {
                tx.Sign(otherKeys, customSignFunction);
            }

            yield return SendRawTransaction(Base16.Encode(tx.ToByteArray(true)), txHash, callback, errorHandlingCallback);
        }

        public static bool IsValidPrivateKey(string key)
        {
            return (key.StartsWith("L", false, CultureInfo.InvariantCulture) ||
                    key.StartsWith("K", false, CultureInfo.InvariantCulture)) && key.Length == 52;
        }

        public static bool IsValidAddress(string address)
        {
            return address.StartsWith("P", false, CultureInfo.InvariantCulture) && address.Length == 45;
        }
    }
}