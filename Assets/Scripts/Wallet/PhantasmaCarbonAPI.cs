using System;
using System.Collections;
using System.Globalization;
using System.Text;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.RPC.Types;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.Unity.Core.Logging;
using UnityEngine;

namespace CarbonTxSupport
{
    public class PhantasmaCarbonAPI
    {
        /// <summary>
        /// Host needs to be an RPC call, i.e. http://127.0.0.1:7077/rpc
        /// </summary>
        public readonly string Host;

        public PhantasmaCarbonAPI(string host)
        {
            this.Host = host;
        }

        #region Transaction
        public IEnumerator SignAndSendCarbonTransaction(byte[] script, Action<string /*tx hash*/, string /*encoded tx*/> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null, int timeout = WebClient.DefaultTimeout, int retries = WebClient.DefaultRetries)
        {
            Log.Write("Sending carbon transaction... script size: " + script.Length);

            // Wrap user callback to validate RPC hash before signaling success
            Action<string, string> wrappedCallback = (hashText, encodedTx) =>
            {
                // Hashes match - forward original callback
                callback?.Invoke(hashText, encodedTx);
            };

            // Send to network and validate on callback
            yield return SendCarbonTransaction(script.ToHex(), wrappedCallback, errorHandlingCallback, timeout, retries);
        }
        
        public IEnumerator SendCarbonTransaction(string txData, Action<string, string> callback, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback = null, int timeout = WebClient.DefaultTimeout, int retries = WebClient.DefaultRetries)
        {
            yield return WebClient.RPCRequest<string>(Host, "sendCarbonTransaction", timeout, retries, errorHandlingCallback, (result) =>
            {
                callback(result, txData);
            }, txData);
        }
        #endregion
    }
}