using System;
using System.Collections;
using System.Globalization;
using LunarLabs.Parser;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Phantasma.Core.Cryptography;
using Phantasma.Core.Numerics;
using Phantasma.Core.Domain;
using Phantasma.Business.Blockchain.Storage;
using Phantasma.Core.Types;
using Newtonsoft.Json;

namespace Phantasma.SDK
{
    public class PaginatedResult<T>
    {
        public uint page { get; set; }
        public uint pageSize { get; set; }
        public uint total { get; set; }
        public uint totalPages { get; set; }

        public T result { get; set; }
    }

    public struct Balance
    {
        public string chain; //
        public string amount; //
        public string symbol; //
        public uint decimals; //
        public string[] ids; //
    }

    public struct Interop
    {
        public string local; //
        public string external; //

        public static Interop FromNode(DataNode node)
        {
            Interop result;

            result.local = node.GetString("local");
            result.external = node.GetString("external");

            return result;
        }
    }

    public struct Platform
    {
        public string platform; //
        public string chain; //
        public string fuel; //
        public string[] tokens; //
        public Interop[] interop; //

        public static Platform FromNode(DataNode node)
        {
            Platform result;

            result.platform = node.GetString("platform");
            result.chain = node.GetString("chain");
            result.fuel = node.GetString("fuel");
            var tokens_array = node.GetNode("tokens");
            if (tokens_array != null)
            {
                result.tokens = new string[tokens_array.ChildCount];
                for (int i = 0; i < tokens_array.ChildCount; i++)
                {

                    result.tokens[i] = tokens_array.GetNodeByIndex(i).AsString();
                }
            }
            else
            {
                result.tokens = new string[0];
            }

            var interop_array = node.GetNode("interop");
            if (interop_array != null)
            {
                result.interop = new Interop[interop_array.ChildCount];
                for (int i = 0; i < interop_array.ChildCount; i++)
                {

                    result.interop[i] = Interop.FromNode(interop_array.GetNodeByIndex(i));

                }
            }
            else
            {
                result.interop = new Interop[0];
            }


            return result;
        }
    }

    public struct Stakes
    {
        public string amount; //
        public uint time; //
        public string unclaimed; //
    }

    public struct Storage
    {
        public uint available;
        public uint used; //
        public string avatar; //
        public Archive[] archives; //

        public static Storage FromNode(DataNode node)
        {
            Storage result;

            if (node == null)
            {
                result.archives = null;
                result.available = 0;
                result.avatar = null;
                result.used = 0;
                return result;
            }

            result.available = node.GetUInt32("available");
            result.used = node.GetUInt32("used");
            result.avatar = node.GetString("avatar");

            var archive_array = node.GetNode("archives");
            if (archive_array != null)
            {
                result.archives = new Archive[archive_array.ChildCount];
                for (int i = 0; i < archive_array.ChildCount; i++)
                {

                    result.archives[i] = Archive.FromNode(archive_array.GetNodeByIndex(i));

                }
            }
            else
            {
                result.archives = new Archive[0];
            }

            return result;
        }
    }

    public struct Account
    {
        public string address; //
        public string name; //
        public Stakes stakes; //
        public Storage storage; //
        public string relay; //
        public string validator; //
        public Balance[] balances; //
    }

    public struct ContractParameter
    {
        public string name;
        public string type;
    }

    public struct ContractMethod
    {
        public string name;
        public string returnType;
        public ContractParameter[] parameters;
    }

    public struct Contract
    {
        public string address; //
        public string name; //
        public string script; //
        public ContractMethod[] methods;
    }

    public struct Event
    {
        public string address; //
        public string kind; //
        public string contract; //
        public string data; //
    }

    public struct Transaction
    {
        public string hash; //
        public string chainAddress; //
        public uint timestamp; //
        public int confirmations; //
        public int blockHeight; //
        public string blockHash; //
        public string script; //
        public Event[] events; //
        public string result; //
        public string fee; //
        public ExecutionState state;
        public string debugComment;
    }

    public struct AccountTransactions
    {
        public string address; //
        public Transaction[] txs; //
    }

    public class TokenPlatform
    {
        public string platform;
        public string hash;

        public static TokenPlatform FromNode(DataNode node)
        {
            var result = new TokenPlatform();

            result.platform = node.GetString("platform");
            result.hash = node.GetString("hash");

            return result;
        }

        public override string ToString()
        {
            return $"Platform {platform}, hash {hash}";
        }
    }

    public class Token
    {
        public string symbol; //
        public string apiSymbol; // API symbols may differ.
        public string name; //
        public int decimals; //
        public string currentSupply; //
        public string maxSupply; //
        public string burnedSupply;
        public string address;
        public string owner;
        public string flags; //
        public string script;
        public TokenPlatform[] external;
        // TODO check if needed after refactoring
        public bool mainnetToken = true;

        public bool IsBurnable()
        {
            return flags.Contains(TokenFlags.Burnable.ToString());
        }
        public bool IsFungible()
        {
            return flags.Contains(TokenFlags.Fungible.ToString());
        }
        public bool IsTransferable()
        {
            return flags.Contains(TokenFlags.Transferable.ToString());
        }
        public bool IsSwappable()
        {
            return flags.Contains(TokenFlags.Swappable.ToString());
        }

        public override string ToString()
        {
            var platforms = "";
            if (external != null)
            {
                foreach (var platform in external)
                {
                    platforms += "\t" + platform.ToString() + "\n";
                }
            }
            return $"Symbol {symbol} ({name}), decimals {decimals}, supplies {currentSupply}/{maxSupply}/{burnedSupply}, flags '{flags}', coinGeckoId '{apiSymbol}', mainnetToken '{mainnetToken}'. Platforms:\n{platforms}";
        }
    }

    public struct TokenProperty
    {
        public string Key;
        public string Value;
    }
    public enum TokenStatus
    {
        Active,
        Infused
    }
    public struct TokenData
    {
        public string ID;
        public string series;
        public uint mint;
        public string chainName;
        public string ownerAddress;
        public string creatorAddress;
        [JsonConverter(typeof(HexByteArrayConverter))]
        public byte[] ram;
        [JsonConverter(typeof(HexByteArrayConverter))]
        public byte[] rom;
        public TokenStatus status;
        public IRom parsedRom;
        public TokenProperty[] infusion;
        public List<TokenProperty> properties;

        public void ParseRoms(string symbol)
        {
            // Pasring ROM
            switch (symbol)
            {
                case "CROWN":
                    parsedRom = new CrownRom(rom, ID);
                    break;
                default:
                    parsedRom = new CustomRom(rom);
                    break;
            }
        }

        public string GetPropertyValue(string key)
        {
            if (properties != null)
            {
                return properties.Where(x => x.Key.ToUpperInvariant() == key.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();
            }

            return null;
        }
    }

    public interface IRom
    {
        string GetName();
        string GetDescription();
        DateTime GetDate();
    }
    public class CrownRom : IRom
    {
        public string tokenId;
        public Address staker;
        public Timestamp date;

        public CrownRom(byte[] rom, string tokenId)
        {
            this.tokenId = tokenId;

            using (var stream = new System.IO.MemoryStream(rom))
            {
                using (var reader = new System.IO.BinaryReader(stream))
                {
                    UnserializeData(reader);
                }
            }
        }

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
        Dictionary<VMObject, VMObject> fields = new Dictionary<VMObject, VMObject>();

        public CustomRom(byte[] romBytes)
        {
            try
            {
                var rom = VMObject.FromBytes(romBytes);
                if (rom.Type == VMType.Struct)
                {
                    fields = (Dictionary<VMObject, VMObject>)rom.Data;
                }
                else
                {
                    Log.Write($"Cannot parse ROM.");
                }
            }
            catch (Exception e)
            {
                Log.Write($"ROM parsing error: {e.ToString()}");
            }
        }

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

    public struct Oracle
    {
        public string url; //
        public string content; //

        public static Oracle FromNode(DataNode node)
        {
            Oracle result;

            result.url = node.GetString("url");
            result.content = node.GetString("content");

            return result;
        }
    }

    public struct Script
    {
        public Event[] events; //
        public string result; //
        public string[] results; //
        public Oracle[] oracles; //
    }

    public struct Archive
    {
        public string hash; //
        public string name; //
        public uint size; //
        public uint time; //
        public string flags; //
        public IArchiveEncryption encryption; //
        public int blockCount; //
        public string[] metadata; //

        public static Archive FromNode(DataNode node)
        {
            Archive result;

            result.hash = node.GetString("hash");
            result.name = node.GetString("name");
            result.size = node.GetUInt32("size");
            result.time = node.GetUInt32("time");
            result.flags = node.GetString("flags");
            result.encryption = ArchiveExtensions.ReadArchiveEncryption(Base16.Decode(node.GetString("encryption")));
            result.blockCount = node.GetInt32("blockCount");
            var metadata_array = node.GetNode("metadata");
            if (metadata_array != null)
            {
                result.metadata = new string[metadata_array.ChildCount];
                for (int i = 0; i < metadata_array.ChildCount; i++)
                {

                    result.metadata[i] = metadata_array.GetNodeByIndex(i).AsString();
                }
            }
            else
            {
                result.metadata = new string[0];
            }


            return result;
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
        public IEnumerator GetAccount(string addressText, Action<Account> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<Account>(Host, "getAccount", WebClient.DefaultTimeout, 0, errorHandlingCallback, (account) =>
            {
                callback(account);
            }, addressText);
        }

        public IEnumerator GetContract(string contractName, Action<Contract> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<Contract>(Host, "getContract", WebClient.DefaultTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, DomainSettings.RootChainName, contractName);
        }


        //Returns the address that owns a given name.
        public IEnumerator LookUpName(string name, Action<string> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest(Host, "lookUpName", WebClient.NoTimeout, 0, errorHandlingCallback, (node) =>
            {
                var result = node.Value;
                callback(result);
            }, name);
        }

        //Returns last X transactions of given address.
        //This api call is paginated, multiple calls might be required to obtain a complete result 
        public IEnumerator GetAddressTransactions(string addressText, uint page, uint pageSize, Action<AccountTransactions, uint, uint> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<PaginatedResult<AccountTransactions>>(Host, "getAddressTransactions", WebClient.NoTimeout, 0, errorHandlingCallback, (paginatedResult) =>
            {
                var currentPage = paginatedResult.page;
                var totalPages = paginatedResult.totalPages;
                callback(paginatedResult.result, currentPage, totalPages);
            }, addressText, page, pageSize);
        }

        //Allows to broadcast a signed operation on the network, but it&apos;s required to build it manually.
        public IEnumerator SendRawTransaction(string txData, Action<string, string> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest(Host, "sendRawTransaction", WebClient.NoTimeout, 0, errorHandlingCallback, (node) =>
            {
                var result = node.Value;
                callback(result, txData);
            }, txData);
        }


        //Allows to invoke script based on network state, without state changes.
        public IEnumerator InvokeRawScript(string chainInput, string scriptData, Action<Script> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<Script>(Host, "invokeRawScript", WebClient.NoTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, chainInput, scriptData);
        }


        //Returns information about a transaction by hash.
        public IEnumerator GetTransaction(string hashText, Action<Transaction> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<Transaction>(Host, "getTransaction", WebClient.NoTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, hashText);
        }


        //Returns an array of tokens deployed in Phantasma.
        public IEnumerator GetTokens(Action<Token[]> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest<Token[]>(Host, "getTokens", 10, 5, errorHandlingCallback, (result) =>
            {
                callback(result);
            });
        }

        private int tokensLoadedSimultaneously = 0;

        //Returns data of a non-fungible token, in hexadecimal format.
        public IEnumerator GetNFT(string symbol, string IDtext, Action<TokenData> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            while (tokensLoadedSimultaneously > 5)
            {
                yield return null;
            }
            tokensLoadedSimultaneously++;

            yield return WebClient.RPCRequest<TokenData>(Host, "getNFT", WebClient.NoTimeout, 0, errorHandlingCallback, (result) =>
            {
                callback(result);
            }, symbol, IDtext, true);

            tokensLoadedSimultaneously--;
        }

        //Writes the contents of an incomplete archive.
        public IEnumerator WriteArchive(string hashText, int blockIndex, byte[] blockContent, Action<Boolean> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null)
        {
            yield return WebClient.RPCRequest(Host, "writeArchive", WebClient.NoTimeout, 0, errorHandlingCallback, (node) =>
            {
                var result = Boolean.Parse(node.Value);
                callback(result);
            }, hashText, blockIndex, Convert.ToBase64String(blockContent));
        }

        public IEnumerator SignAndSendTransactionWithPayload(PhantasmaKeys keys, IKeyPair otherKeys, string nexus, byte[] script, string chain, BigInteger gasPrice, BigInteger gasLimit, byte[] payload, ProofOfWork PoW, Action<string, string> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
        {
            Log.Write("Sending transaction...");

            var tx = new Phantasma.Core.Domain.Transaction(nexus, chain, script,  DateTime.UtcNow + TimeSpan.FromMinutes(20), payload);

            if (PoW != ProofOfWork.None)
            {
                tx.Mine(PoW);
            }

            tx.Sign(keys, null);
            if (otherKeys != null)
            {
                tx.Sign(otherKeys, customSignFunction);
            }

            yield return SendRawTransaction(Base16.Encode(tx.ToByteArray(true)), callback, errorHandlingCallback);
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