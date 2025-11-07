using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.VM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using UnityEngine;

namespace Poltergeist
{

    public class DescriptionUtils : MonoBehaviour
    {
        private static bool CheckIfCallShouldBeIgnored(DisasmMethodCall call)
        {
            return ("gas".Equals(call.ContractName) && (call.MethodName == "AllowGas" || call.MethodName == "SpendGas"));
        }

        private static string GetCallFullName(DisasmMethodCall call)
        {
            if (!string.IsNullOrEmpty(call.ContractName))
                return $"{call.ContractName}.{call.MethodName}";
            else
                return call.MethodName;
        }

        private static bool CompareCalls(DisasmMethodCall call1, DisasmMethodCall call2, params int[] argNumbersToCompare)
        {
            // Compare contract and method names.
            if (GetCallFullName(call1) != GetCallFullName(call2))
                return false;

            for (int i = 0; i < argNumbersToCompare.Length; i++)
            {
                // Compare all arguments as strings.
                if (call1.Arguments[argNumbersToCompare[i]].AsString() != call2.Arguments[argNumbersToCompare[i]].AsString())
                    return false;
            }

            return true;
        }

        private static string GetStringArg(DisasmMethodCall call, int argumentNumber)
        {
            try
            {
                return call.Arguments[argumentNumber].AsString();
            }
            catch (Exception e)
            {
                throw new Exception($"{GetCallFullName(call)}: Error: Cannot get description for argument #{argumentNumber + 1} [String]: {e.Message}");
            }
        }

        private static BigInteger GetNumberArg(DisasmMethodCall call, int argumentNumber)
        {
            try
            {
                return call.Arguments[argumentNumber].AsNumber();
            }
            catch (Exception e)
            {
                throw new Exception($"{GetCallFullName(call)}: Error: Cannot get description for argument #{argumentNumber + 1} [Number]: {e.Message}");
            }
        }

        private static Timestamp GetTimestampArg(DisasmMethodCall call, int argumentNumber)
        {
            try
            {
                return call.Arguments[argumentNumber].AsTimestamp();
            }
            catch (Exception e)
            {
                throw new Exception($"{GetCallFullName(call)}: Error: Cannot get description for argument #{argumentNumber + 1} [Timestamp]: {e.Message}");
            }
        }

        private static byte[] GetByteArrayArg(DisasmMethodCall call, int argumentNumber)
        {
            try
            {
                return call.Arguments[argumentNumber].AsByteArray();
            }
            catch (Exception e)
            {
                throw new Exception($"{GetCallFullName(call)}: Error: Cannot get description for argument #{argumentNumber + 1} [String]: {e.Message}");
            }
        }

        private static List<string> knownContracts = null;
        private static Dictionary<string, int> methodTable = DisasmUtils.GetDefaultDisasmTable();


        public static void RegisterContractMethod(string contractMethod, int paramCount)
        {
            methodTable[contractMethod] = paramCount;
        }

        private static string ShortenTokenId(string tokenId)
        {
            if (String.IsNullOrEmpty(tokenId) || tokenId.Length <= 13)
                return tokenId;

            return tokenId.Substring(0, 5) + "..." + tokenId.Substring(tokenId.Length - 5);
        }

        public static IEnumerator GetDescription(byte[] script, bool devMode, Action<string, string> callback)
        {
            Debug.Log("disam methods: " + string.Join(", ", methodTable.Keys));

            if (knownContracts == null)
            {
                // Collecting known contract names
                knownContracts = methodTable.Keys.Select(x => x.IndexOf(".") > 0 ? x.Substring(0, x.IndexOf(".")) : x).Distinct().ToList();
            }

            var accountManager = AccountManager.Instance;

            IEnumerable<string> contracts;

            try
            {
                contracts = DisasmUtils.ExtractContractNames(script).Where(x => !knownContracts.Contains(x));
            }
            catch (Exception e)
            {
                callback(null, e.ToString());
                yield break;
            }

            var contractsToLoad = contracts.Count();
            var contractsProcessed = 0;
            foreach (var contract in contracts)
            {
                WalletGUI.Instance.StartCoroutine(
                    accountManager.phantasmaApi.GetContract(contract, (contractStruct) =>
                    {
                        Log.Write($"Registering {contractStruct.Methods.Length} methods for {contract}");

                        foreach (var method in contractStruct.Methods)
                        {
                            Log.Write($"Registering contract method {contract}.{method.Name} with {method.Parameters.Length} parameters");
                            DescriptionUtils.RegisterContractMethod($"{contract}.{method.Name}", method.Parameters.Length);
                        }

                        contractsProcessed++;
                    }, (error, msg) =>
                    {
                        Log.WriteWarning("Could not fetch contract info: " + contract);
                        contractsProcessed++;
                    }));
            }

            while (contractsProcessed < contractsToLoad)
            {
                yield return null;
            }

            IEnumerable<DisasmMethodCall> disasm;
            try
            {
                disasm = DisasmUtils.ExtractMethodCalls(script, methodTable);
            }
            catch (Exception e)
            {
                callback(null, e.ToString());
                yield break;
            }

            // Checking if all calls are "market.SellToken" calls only or "Runtime.TransferToken" only,
            // and we can group them.
            int groupSize = 0;
            DisasmMethodCall? prevCall = null;
            foreach (var call in disasm)
            {
                if (CheckIfCallShouldBeIgnored(call) && !devMode)
                {
                    continue;
                }

                if (GetCallFullName(call) == "Runtime.TransferToken")
                {
                    if (prevCall != null && !CompareCalls((DisasmMethodCall)prevCall, call, 0, 1))
                    {
                        groupSize = 0;
                        break;
                    }

                    groupSize++;
                }
                else if (GetCallFullName(call) == "market.SellToken")
                {
                    if (prevCall != null && !CompareCalls((DisasmMethodCall)prevCall, call, 0, 2, 4, 5))
                    {
                        groupSize = 0;
                        break;
                    }

                    groupSize++;
                }
                else
                {
                    // Different call, grouping is not supported.
                    groupSize = 0;
                    break;
                }

                prevCall = call;
            }

            int transferTokenCounter = 0; // Counting "market.TransferToken" calls, needed for grouping.
            int sellTokenCounter = 0; // Counting "market.SellToken" calls, needed for grouping.

            var sb = new StringBuilder();
            foreach (var entry in disasm)
            {
                if (CheckIfCallShouldBeIgnored(entry) && !devMode)
                {
                    continue;
                }

                // Put it to log so that developer can easily check what PG is receiving.
                var unprocessedMethodCall = "Unprocessed method call: " + entry.ToString();
                Log.Write(unprocessedMethodCall);
                if (devMode)
                {
                    sb.AppendLine(unprocessedMethodCall);
                    sb.AppendLine();
                }

                switch (GetCallFullName(entry))
                {
                    case "gas.AllowGas":
                        {
                            string from, to;
                            if (entry.Arguments[0].Type.ToString() == "Bytes")
                            {
                                from = Serialization.Unserialize(GetByteArrayArg(entry, 0), typeof(Address)).ToString();
                            }
                            else
                            {
                                from = GetStringArg(entry, 0);
                            }
                            if (entry.Arguments[1].Type.ToString() == "Bytes")
                            {
                                to = Serialization.Unserialize(GetByteArrayArg(entry, 1), typeof(Address)).ToString();
                            }
                            else
                            {
                                to = GetStringArg(entry, 1);
                            }
                            var gasPrice = GetNumberArg(entry, 2);
                            var gasLimit = GetNumberArg(entry, 3);

                            sb.AppendLine($"\u2605 AllowGas price: {gasPrice} limit: {gasLimit} from {from} to {to}.");

                            break;
                        }

                    case "gas.SpendGas":
                        {
                            string address;
                            if (entry.Arguments[0].Type.ToString() == "Bytes")
                            {
                                address = Serialization.Unserialize(GetByteArrayArg(entry, 0), typeof(Address)).ToString();
                            }
                            else
                            {
                                address = GetStringArg(entry, 0);
                            }

                            sb.AppendLine($"\u2605 SpendGas address {address}.");

                            break;
                        }

                    case "Runtime.TransferToken":
                        {
                            var src = GetStringArg(entry, 0);
                            var dst = GetStringArg(entry, 1);
                            var symbol = GetStringArg(entry, 2);
                            var nftNumber = GetStringArg(entry, 3);

                            if (groupSize > 1)
                            {
                                if (transferTokenCounter == 0)
                                {
                                    // Desc line #1.
                                    sb.AppendLine("\u2605 Transfer:");
                                }

                                if (transferTokenCounter == groupSize - 1)
                                {
                                    // Desc line for token #N.
                                    sb.AppendLine($"{symbol} NFT #{ShortenTokenId(nftNumber)}");
                                    sb.AppendLine($"to {dst}.");
                                }
                                else
                                {
                                    // Desc line for tokens #1 ... N-1.
                                    sb.AppendLine($"{symbol} NFT #{ShortenTokenId(nftNumber)},");
                                }
                            }
                            else
                            {
                                sb.AppendLine($"\u2605 Transfer {symbol} NFT #{ShortenTokenId(nftNumber)} to {dst}.");
                            }

                            transferTokenCounter++;

                            break;
                        }
                    case "Runtime.TransferTokens":
                        {
                            string src, dst;
                            if (entry.Arguments[0].Type.ToString() == "Bytes")
                            {
                                src = Serialization.Unserialize(GetByteArrayArg(entry, 0), typeof(Address)).ToString();
                            }
                            else
                            {
                                src = GetStringArg(entry, 0);
                            }
                            if (entry.Arguments[1].Type.ToString() == "Bytes")
                            {
                                dst = Serialization.Unserialize(GetByteArrayArg(entry, 1), typeof(Address)).ToString();
                            }
                            else
                            {
                                dst = GetStringArg(entry, 1);
                            }
                            var symbol = GetStringArg(entry, 2);
                            var amount = GetNumberArg(entry, 3);

                            var token = Tokens.GetToken(symbol, PlatformKind.Phantasma);

                            var total = UnitConversion.ToDecimal(amount, token.Decimals);

                            sb.AppendLine($"\u2605 Transfer {total} {symbol} from {src} to {dst}.");
                            break;
                        }
                    case "market.BuyToken":
                        {
                            var dst = GetStringArg(entry, 0);
                            var symbol = GetStringArg(entry, 1);
                            var nftNumber = GetStringArg(entry, 2);

                            sb.AppendLine($"\u2605 Buy {symbol} NFT #{ShortenTokenId(nftNumber)}.");
                            break;
                        }
                    case "market.CancelSale":
                        {
                            var symbol = GetStringArg(entry, 0);
                            var nftNumber = GetStringArg(entry, 1);

                            sb.AppendLine($"\u2605 Cancel sale of {symbol} NFT #{ShortenTokenId(nftNumber)}.");
                            break;
                        }
                    case "market.SellToken":
                        {
                            var dst = GetStringArg(entry, 0);
                            var tokenSymbol = GetStringArg(entry, 1);
                            var priceSymbol = GetStringArg(entry, 2);
                            var nftNumber = GetStringArg(entry, 3);

                            var priceToken = Tokens.GetToken(priceSymbol, PlatformKind.Phantasma);

                            var price = UnitConversion.ToDecimal(GetNumberArg(entry, 4), priceToken.Decimals);

                            var untilDate = GetTimestampArg(entry, 5);

                            if (groupSize > 1)
                            {
                                if (sellTokenCounter == 0)
                                {
                                    // Desc line #1.
                                    sb.AppendLine("\u2605 Sell:");
                                    sb.AppendLine($"{tokenSymbol} NFT #{ShortenTokenId(nftNumber)},");
                                }
                                else if (sellTokenCounter == groupSize - 1)
                                {
                                    // Desc line #N.
                                    sb.AppendLine($"{tokenSymbol} NFT #{ShortenTokenId(nftNumber)}");
                                    sb.AppendLine($"for {price} {priceSymbol} each, offer valid until {untilDate}.");
                                }
                                else
                                {
                                    // Desc lines #2 ... N-1.
                                    sb.AppendLine($"{tokenSymbol} NFT #{ShortenTokenId(nftNumber)},");
                                }
                            }
                            else
                            {
                                sb.AppendLine($"\u2605 Sell {tokenSymbol} NFT #{ShortenTokenId(nftNumber)} for {price} {priceSymbol}, offer valid until {untilDate}.");
                            }

                            sellTokenCounter++;

                            break;
                        }
                    case "market.EditAuction":
                        {
                            var dst = GetStringArg(entry, 0);
                            var tokenSymbol = GetStringArg(entry, 1);
                            var priceSymbol = GetStringArg(entry, 2);
                            var nftNumber = GetStringArg(entry, 3);

                            var priceToken = Tokens.GetToken(priceSymbol, PlatformKind.Phantasma);

                            var price = UnitConversion.ToDecimal(GetNumberArg(entry, 4), priceToken.Decimals);
                            var endPrice = UnitConversion.ToDecimal(GetNumberArg(entry, 5), priceToken.Decimals);

                            var startDate = GetTimestampArg(entry, 6);
                            var untilDate = GetTimestampArg(entry, 7);
                            var extensionPeriod = GetStringArg(entry, 8);

                            sb.AppendLine($"\u2605 Edit {tokenSymbol} NFT #{ShortenTokenId(nftNumber)} Auction.");
                            break;
                        }
                    case "market.ListToken":
                        {
                            var dst = GetStringArg(entry, 0);
                            var tokenSymbol = GetStringArg(entry, 1);
                            var priceSymbol = GetStringArg(entry, 2);
                            var nftNumber = GetStringArg(entry, 3);

                            var priceToken = Tokens.GetToken(priceSymbol, PlatformKind.Phantasma);

                            var price = UnitConversion.ToDecimal(GetNumberArg(entry, 4), priceToken.Decimals);
                            var endPrice = UnitConversion.ToDecimal(GetNumberArg(entry, 5), priceToken.Decimals);

                            var startDate = GetTimestampArg(entry, 6);
                            var untilDate = GetTimestampArg(entry, 7);
                            var extensionPeriod = GetStringArg(entry, 8);
                            var typeAuction = GetNumberArg(entry, 9);
                            var listingFee = GetStringArg(entry, 10);
                            var listingFeeAddress = GetStringArg(entry, 11);

                            if (typeAuction == 0)
                            {
                                sb.AppendLine($"\u2605 List {tokenSymbol} NFT #{ShortenTokenId(nftNumber)} for a Fixed Auction with a price of {price} {priceSymbol}.");
                                if (devMode)
                                {
                                    sb.AppendLine($"Start date: {startDate} [unix seconds: {startDate.Value}].");
                                    sb.AppendLine($"End date: {untilDate} [unix seconds: {untilDate.Value}].");
                                    sb.AppendLine($"Extension period: {extensionPeriod}.");
                                }
                                break;
                            }
                            else if (typeAuction == 1)
                            {
                                sb.AppendLine($"\u2605 List {tokenSymbol} NFT #{ShortenTokenId(nftNumber)} for a Classic Auction with a starting price of {price} {priceSymbol}.");
                                break;
                            }
                            else if (typeAuction == 2)
                            {
                                sb.AppendLine($"\u2605 List {tokenSymbol} NFT #{ShortenTokenId(nftNumber)} for a Reserve Auction with a reserve price of {price} {priceSymbol}.");
                                break;
                            }
                            else if (typeAuction == 3)
                            {
                                sb.AppendLine($"\u2605 List {tokenSymbol} NFT #{ShortenTokenId(nftNumber)} for a Dutch Auction with a starting price of {price} {priceSymbol} and an end price of {endPrice} {priceSymbol}.");
                                break;
                            }
                            break;
                        }
                    case "market.BidToken":
                        {
                            var dst = GetStringArg(entry, 0);
                            var tokenSymbol = GetStringArg(entry, 1);
                            var nftNumber = GetStringArg(entry, 2);

                            //Token priceToken;
                            //accountManager.GetTokenBySymbol(priceSymbol, PlatformKind.Phantasma, out priceToken);

                            //var price = UnitConversion.ToDecimal(GetNumberArg(entry, 3), priceToken.decimals);

                            var buyingFee = GetStringArg(entry, 4);
                            var buyingFeeAddress = GetStringArg(entry, 5);


                            sb.AppendLine($"\u2605 Bid or Buy {tokenSymbol} NFT #{ShortenTokenId(nftNumber)}.");
                            break;
                        }
                    case "sale.Purchase":
                        {
                            var saleHash = GetStringArg(entry, 1);
                            var tokenSymbol = GetStringArg(entry, 2);
                            var tokenAmount = GetStringArg(entry, 3);

                            var priceToken = Tokens.GetToken(tokenSymbol, PlatformKind.Phantasma);

                            var purchase = UnitConversion.ToDecimal(GetNumberArg(entry, 4), priceToken.Decimals);

                            sb.AppendLine($"\u2605 Participate to sale {saleHash} with {purchase} {tokenSymbol}.");
                            break;
                        }
                    case "Runtime.MintToken":
                        {
                            var owner = GetStringArg(entry, 0);
                            var recepient = GetStringArg(entry, 1);
                            var symbol = GetStringArg(entry, 2);
                            var bytes = GetByteArrayArg(entry, 3);

                            sb.AppendLine($"\u2605 Mint {symbol} NFT from {owner} with {recepient} as recipient.");
                            break;
                        }
                    case "Runtime.BurnTokens":
                        {
                            var address = GetStringArg(entry, 0);
                            var symbol = GetStringArg(entry, 1);
                            var amount = GetNumberArg(entry, 2);

                            var token = Tokens.GetToken(symbol, PlatformKind.Phantasma);

                            var total = UnitConversion.ToDecimal(amount, token.Decimals);

                            sb.AppendLine($"\u2605 Burn {total} {symbol} from {address}.");
                            break;
                        }
                    case "Runtime.BurnToken":
                        {
                            var address = GetStringArg(entry, 0);
                            var symbol = GetStringArg(entry, 1);
                            var nftNumber = GetStringArg(entry, 2);

                            sb.AppendLine($"\u2605 Burn {symbol} NFT #{ShortenTokenId(nftNumber)} from {address}.");
                            break;
                        }
                    case "Runtime.InfuseToken":
                        {
                            var address = GetStringArg(entry, 0);
                            var targetSymbol = GetStringArg(entry, 1);
                            var tokenID = GetStringArg(entry, 2);
                            var infuseSymbol = GetStringArg(entry, 3);
                            var amount = GetNumberArg(entry, 4);
                            var amountString = amount.ToString();

                            var infuseToken = Tokens.GetToken(infuseSymbol, PlatformKind.Phantasma);

                            sb.AppendLine($"\u2605 Infuse {targetSymbol} NFT #{ShortenTokenId(tokenID)} with " + (infuseToken.IsFungible() ? $"{UnitConversion.ToDecimal(amount, infuseToken.Decimals)} {infuseSymbol}." : $"{infuseSymbol} NFT #{ShortenTokenId(amountString)}."));
                            break;
                        }
                    case "Nexus.CreateToken":
                        {
                            var address = GetStringArg(entry, 0);
                            var symbol = GetStringArg(entry, 1);
                            var name = GetStringArg(entry, 2);
                            var maxSupply = GetNumberArg(entry, 3);
                            var decimals = GetNumberArg(entry, 4);
                            var flags = GetNumberArg(entry, 5);
                            var ctScript = GetByteArrayArg(entry, 6);

                            sb.AppendLine($"\u2605 Create token {symbol} with name '{name}', {maxSupply} max supply, {decimals} decimals from {address}.");
                            break;
                        }

                    case "Nexus.CreateOrganization":
                        {
                            var address = GetStringArg(entry, 0);
                            var id = GetStringArg(entry, 1);
                            var name = GetStringArg(entry, 2);
                            var _script = GetByteArrayArg(entry, 3);
                            sb.AppendLine($"\u2605 {address} -> Create Organization '{id}' with name '{name}', with script: {_script}.");
                            break;
                        }

                    case "Organization.AddMember":
                        {
                            var address = GetStringArg(entry, 0);
                            var org_id = GetStringArg(entry, 1);
                            var target = GetStringArg(entry, 2);
                            sb.AppendLine($"\u2605 {address} -> Adding {target} to Organization '{org_id}'.");
                            break;
                        }

                    case "Organization.RemoveMember":
                        {
                            var address = GetStringArg(entry, 0);
                            var org_id = GetStringArg(entry, 1);
                            var target = GetStringArg(entry, 2);
                            sb.AppendLine($"\u2605 {address} -> Removing {target} from Organization '{org_id}'.");
                            break;
                        }

                    case "GHOST.getLockedContent":
                        {
                            var nftSymbol = GetStringArg(entry, 0);
                            var nftID = GetStringArg(entry, 1);

                            sb.AppendLine($"\u2605 Get locked content for {nftSymbol} #{ShortenTokenId(nftID)}.");
                            break;
                        }

                    case "GHOST.mintToken":
                        {
                            var editionId = GetNumberArg(entry, 0);
                            var editionMax = GetNumberArg(entry, 1);
                            var editionMode = GetNumberArg(entry, 2);
                            var creator = GetStringArg(entry, 3);
                            var royalties = GetNumberArg(entry, 4);
                            var mintTicker = GetStringArg(entry, 5);
                            var numOfNfts = GetNumberArg(entry, 6);
                            var name = GetStringArg(entry, 7);
                            var description = GetStringArg(entry, 8);
                            var type = GetStringArg(entry, 9);
                            var imageURL = GetStringArg(entry, 10);
                            var infoURL = GetStringArg(entry, 11);
                            var attributeType1 = GetStringArg(entry, 12);
                            var attributeValue1 = GetStringArg(entry, 13);
                            var attributeType2 = GetStringArg(entry, 14);
                            var attributeValue2 = GetStringArg(entry, 15);
                            var attributeType3 = GetStringArg(entry, 16);
                            var attributeValue3 = GetStringArg(entry, 17);
                            var lockedContent = GetStringArg(entry, 18);
                            var listPrice = GetNumberArg(entry, 19);
                            var listPriceCurrency = GetStringArg(entry, 20);
                            var listLastEndDate = GetTimestampArg(entry, 21);
                            var infusedAsset = GetStringArg(entry, 22);
                            var infusedAmount = GetNumberArg(entry, 23);
                            var hasLocked = GetStringArg(entry, 24);

                            if (editionId > 0)
                            {
                                sb.AppendLine($"\u2605 Mint on existing series #{editionId}, a total of {numOfNfts}x {mintTicker}.");
                            }
                            else
                            {
                                sb.AppendLine($"\u2605 Mint on a new series {numOfNfts}x {mintTicker}, with a {royalties}% royalty and named: {name}.");
                            }
                            if (infusedAmount > 0)
                            {
                                var infusedToken = Tokens.GetToken(infusedAsset, PlatformKind.Phantasma);
                                var infusedAmountWithDecimals = infusedToken.IsFungible() ? UnitConversion.ToDecimal(infusedAmount, infusedToken.Decimals) : 0;

                                sb.AppendLine($"\u2605 Infuse {numOfNfts}x {mintTicker} with {(infusedAmountWithDecimals > 0 ? infusedAmountWithDecimals.ToString() : infusedAmount.ToString())} {infusedAsset} each.");
                            }
                            if (listPrice > 0)
                            {
                                var listPriceToken = Tokens.GetToken(listPriceCurrency, PlatformKind.Phantasma);
                                var listPriceWithDecimals = (listPrice > 0) ? UnitConversion.ToDecimal(listPrice, listPriceToken.Decimals) : 0;

                                sb.AppendLine($"\u2605 Sell {numOfNfts}x {mintTicker}, for {listPriceWithDecimals} {listPriceCurrency}, offer valid until {listLastEndDate}.");
                            }
                            break;
                        }

                    case "pharming.claim":
                        {
                            var address = GetStringArg(entry, 0);
                            var symbol1 = GetStringArg(entry, 1);
                            var symbol2 = GetStringArg(entry, 2);

                            sb.AppendLine($"\u2605 Claiming {symbol1}/{symbol2} Pool rewards");
                            break;
                        }

                    default:
                        sb.AppendLine(entry.ToString());
                        break;
                }

                sb.AppendLine();
            }

            if (sb.Length > 0)
            {
                callback(sb.ToString(), null);
                yield break;
            }

            callback(null, "Unknown transaction content.");
        }

        // TODO move to SDK, also we have 1 copy of this method in RPC
        private static string VmDynamicVariableToString(VmDynamicVariable v)
        {
            switch(v.type)
            {
                case VmType.String:
                    return v.GetString();
                case VmType.Int8:
                    return v.GetInt8().ToString();
                case VmType.Int16:
                    return v.GetUInt16().ToString();
                case VmType.Int32:
                    return v.GetUInt32().ToString();
                case VmType.Int64:
                    return v.GetUInt64().ToString();
                case VmType.Int256:
                    return v.GetUInt256().ToString();
                case VmType.Bytes:
                    return v.GetBytes().ToHex();
                case VmType.Bytes16:
                    return v.data == null ? "" : ((Bytes16)v.data).bytes.ToHex();
                case VmType.Bytes32:
                    return v.data == null ? "" : ((Bytes32)v.data).bytes.ToHex();
                case VmType.Bytes64:
                    return v.data == null ? "" : ((Bytes64)v.data).bytes.ToHex();
                default:
                    return $"(unsupported VmDynamicVariable type {v.type})";
            }
        }

        public static void FromTxMsgCall(TxMsgCall call, ref StringBuilder sb)
        {
            switch ((ModuleId)call.moduleId)
            {
                case ModuleId.Governance:
                    {
                        sb.AppendLine("Governance module: Unknown call");
                        break;
                    }
                case ModuleId.Token:
                    {
                        switch ((TokenContract_Methods)call.methodId)
                        {
                            case TokenContract_Methods.CreateToken:
                                {
                                    var tokenInfo = CarbonBlob.New<TokenInfo>(call.args);
                                    sb.AppendLine($"\u2605 Create token with symbol {tokenInfo.symbol.data}");

                                    sb.AppendLine();
                                    sb.AppendLine($"Owner: {Address.FromBytes(tokenInfo.owner.bytes)}");
                                    sb.AppendLine($"Max supply: {tokenInfo.maxSupply}");
                                    sb.AppendLine($"Decimals: {tokenInfo.decimals}");
                                    sb.AppendLine($"Is NFT: {tokenInfo.flags == PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.TokenFlags.NonFungible}");

                                    sb.AppendLine();
                                    sb.AppendLine($"METADATA:");
                                    var metadata = CarbonBlob.New<VmDynamicStruct>(tokenInfo.metadata);
                                    foreach (var f in metadata.fields)
                                    {
                                        var value = VmDynamicVariableToString(f.value);
                                        if (value.Length > 100)
                                        {
                                            value = $"{value.Substring(0, 100)}... [Value is too long: {value.Length}]";
                                        }
                                        sb.AppendLine();
                                        sb.AppendLine($"\u2022 {f.name.data}: {value}");
                                    }

                                    break;
                                }
                            case TokenContract_Methods.CreateTokenSeries:
                                {
                                    // Skipping 8 bytes of token ID
                                    var seriesInfo = CarbonBlob.New<SeriesInfo>(call.args, 8);

                                    sb.AppendLine($"\u2605 Create series");
                                    sb.AppendLine($"Max mint: {seriesInfo.maxMint}");
                                    sb.AppendLine($"Max supply: {seriesInfo.maxSupply}");

                                    break;
                                }
                            case TokenContract_Methods.MintNonFungible:
                                {
                                    sb.AppendLine("Token module: MintNonFungible");
                                    break;
                                }
                            case TokenContract_Methods.MintFungible:
                                {
                                    sb.AppendLine("Token module: MintFungible");
                                    break;
                                }
                            case TokenContract_Methods.TransferFungible:
                                {
                                    var transfer = CarbonBlob.New<TxMsgTransferFungible>(call.args);
                                    sb.AppendLine("Token module: TransferFungible: tokenId: " + transfer.tokenId + " amount: " + transfer.amount);
                                    break;
                                }
                            default:
                                sb.AppendLine($"Token module: {(TokenContract_Methods)call.methodId}");
                                break;
                        }
                        break;
                    }
                default:
                    sb.AppendLine($"Unknown module {call.moduleId} call");
                    break;
            }
        }

        public static void FromTxMsgCall_Multi(TxMsgCall_Multi call, ref StringBuilder sb)
        {
            foreach (var c in call.calls)
            {
                FromTxMsgCall(c, ref sb);
            }
            return;
        }

        public static IEnumerator GetCarbonDescription(TxMsg txMsg, bool devMode, Action<string, string> callback)
        {
            var sb = new StringBuilder();

            switch (txMsg.type)
            {
                case TxTypes.Call:
                    {
                        FromTxMsgCall((TxMsgCall)txMsg.msg, ref sb);
                        break;
                    }
                case TxTypes.Call_Multi:
                    {
                        FromTxMsgCall_Multi((TxMsgCall_Multi)txMsg.msg, ref sb);
                        break;
                    }
                case TxTypes.MintFungible:
                    {
                        var mint = (TxMsgMintFungible)txMsg.msg;

                        sb.AppendLine($"\u2605 Fungible token mint");
                        sb.AppendLine($"To address: {Address.FromBytes(mint.to.bytes)}");
                        sb.AppendLine($"Amount: {mint.amount}");
                        break;
                    }
                case TxTypes.Phantasma:
                    sb.AppendLine("Script tx: Unknown");
                    // var txMsgPhantasma = (TxMsgPhantasma)txMsg.msg;
                    // TODO describe txMsgPhantasma.script;
                    break;
                case TxTypes.TransferFungible:
                    {
                        sb.AppendLine("Token module: TransferFungible");
                        break;
                    }
                case TxTypes.MintNonFungible:
                    {
                        var mint = (TxMsgMintNonFungible)txMsg.msg;

                        sb.AppendLine($"\u2605 NFT mint");
                        sb.AppendLine($"To address: {Address.FromBytes(mint.to.bytes)}");
                        break;
                    }
                case TxTypes.TransferFungible_GasPayer:
                case TxTypes.TransferNonFungible_Single:
                case TxTypes.TransferNonFungible_Single_GasPayer:
                case TxTypes.TransferNonFungible_Multi:
                case TxTypes.TransferNonFungible_Multi_GasPayer:
                case TxTypes.BurnFungible:
                case TxTypes.BurnFungible_GasPayer:
                case TxTypes.BurnNonFungible:
                case TxTypes.BurnNonFungible_GasPayer:
                case TxTypes.Trade:
                    sb.AppendLine($"Call: {txMsg.type}");
                    break;
                default:
                    sb.AppendLine($"Unknown call: {txMsg.type}");
                    break;
            }


            if (sb.Length > 0)
            {
                callback(sb.ToString(), null);
                yield break;
            }

            callback(null, "Unknown transaction content.");
        }
    }
}
