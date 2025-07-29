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
    public class PhantasmaAPI
    {
        public readonly string Host;

        public PhantasmaAPI(string host)
        {
            this.Host = host;
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

        public IEnumerator SignAndSendTransactionWithPayload(IKeyPair keys, string nexus, byte[] script, string chain, byte[] payload, Action<string, string, Hash> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
        {
            Log.Write("Sending transaction... script size: " + script.Length);

            var tx = new PhantasmaPhoenix.Protocol.Transaction(nexus, chain, script, DateTime.UtcNow + TimeSpan.FromMinutes(20), payload);

            Hash txHash = tx.SignEx(keys, customSignFunction);

            yield return SendRawTransaction(Base16.Encode(tx.ToByteArray(true)), txHash, callback, errorHandlingCallback);
        }
    }
}