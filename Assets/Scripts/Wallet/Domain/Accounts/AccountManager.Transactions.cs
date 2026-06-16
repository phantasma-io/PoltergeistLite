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
        public void SignAndSendTransaction(string chain, byte[] script, byte[] payload, Action<Hash, string> callback, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
        {
            async Task ExecuteAsync()
            {
                if (payload == null)
                {
                    payload = System.Text.Encoding.UTF8.GetBytes(WalletIdentifier);
                }

                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            try
                            {
                                var result = await AsyncPhantasma.FromApi(
                                    (Action<string, string> onSuccess, Action<EPHANTASMA_SDK_ERROR_TYPE, string> onError) =>
                                        phantasmaApi.SignAndSendTransaction(
                                            PhantasmaKeys.FromWIF(CurrentWif),
                                            Settings.nexusName,
                                            script,
                                            chain,
                                            payload,
                                            onSuccess,
                                            onError,
                                            customSignFunction,
                                            timeout: WebClient.DefaultTimeout,
                                            retries: NetworkRetryPolicy.Retries),
                                    CancellationToken.None);

                                var hashText = result.Item1;
                                var encodedTx = result.Item2;

                                if (Settings.devMode)
                                {
                                    Log.Write($"SignAndSendTransactionWithPayload(): Encoded tx: {encodedTx}");
                                }

                                if (!string.IsNullOrEmpty(hashText))
                                {
                                    try
                                    {
                                        callback(Hash.Parse(hashText), null);
                                    }
                                    catch (Exception e)
                                    {
                                        Log.WriteWarning("Error parsing hash: " + e.Message);
                                        callback(Hash.Null, $"Error: hashText={hashText}");
                                    }
                                }
                                else
                                {
                                    callback(Hash.Null, "Failed to send transaction");
                                }
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);

                                callback(Hash.Null, ex.Message);
                            }

                            break;
                        }

                    default:
                        {
                            callback(Hash.Null, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }

            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void SignAndSendCarbonTransaction(TxMsg tx, Action<Hash, string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            try
                            {
                                var result = await AsyncPhantasma.FromApi(
                                    (Action<string, string> onSuccess, Action<EPHANTASMA_SDK_ERROR_TYPE, string> onError) =>
                                        phantasmaApi.SignAndSendCarbonTransaction(
                                            PhantasmaKeys.FromWIF(CurrentWif),
                                            tx,
                                            onSuccess,
                                            onError,
                                            timeout: WebClient.DefaultTimeout,
                                            retries: NetworkRetryPolicy.Retries),
                                    CancellationToken.None);

                                var hashText = result.Item1;
                                var encodedTx = result.Item2;

                                if (Settings.devMode)
                                {
                                    Log.Write($"SignAndSendCarbonTransaction(): Encoded tx: {encodedTx}");
                                }

                                if (!string.IsNullOrEmpty(hashText))
                                {
                                    try
                                    {
                                        callback(Hash.Parse(hashText), null);
                                    }
                                    catch (Exception e)
                                    {
                                        Log.WriteWarning("Error parsing hash: " + e.Message);
                                        callback(Hash.Null, $"Error: hashText={hashText}");
                                    }
                                }
                                else
                                {
                                    callback(Hash.Null, "Failed to send transaction");
                                }
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);

                                callback(Hash.Null, ex.Message);
                            }

                            break;
                        }

                    default:
                        {
                            callback(Hash.Null, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void InvokeScript(string chain, byte[] script, Action<string[], string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            Log.Write("InvokeScript: " + System.Text.Encoding.UTF8.GetString(script), Log.Level.Debug1);
                            try
                            {
                                var result = await AsyncPhantasma.FromApi<PhantasmaPhoenix.RPC.Models.ScriptResult>(
                                    (onSuccess, onError) => phantasmaApi.InvokeRawScript(
                                        chain,
                                        Base16.Encode(script),
                                        onSuccess,
                                        onError,
                                        timeout: WebClient.DefaultTimeout,
                                        retries: NetworkRetryPolicy.Retries),
                                    CancellationToken.None);

                                Log.Write("InvokeScript result: " + result.Result, Log.Level.Debug1);
                                callback(result.Results, null);
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);
                                callback(null, ex.Message);
                            }

                            break;
                        }
                    default:
                        {
                            callback(null, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void InvokeScriptPhantasma(string chain, byte[] script, Action<byte[], string> callback)
        {
            async Task ExecuteAsync()
            {
                Log.Write("InvokeScriptPhantasma: " + System.Text.Encoding.UTF8.GetString(script), Log.Level.Debug1);
                try
                {
                    var result = await AsyncPhantasma.FromApi<PhantasmaPhoenix.RPC.Models.ScriptResult>(
                        (onSuccess, onError) => phantasmaApi.InvokeRawScript(
                            chain,
                            Base16.Encode(script),
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        CancellationToken.None);
                    Log.Write("InvokeScriptPhantasma result: " + result.Result, Log.Level.Debug1);
                    callback(Base16.Decode(result.Result), null);
                }
                catch (PhantasmaRequestException ex)
                {
                    RotateRpcOnWebError(ex);
                    callback(null, ex.Message);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            Log.Write("WriteArchive: " + hash, Log.Level.Debug1);
                            try
                            {
                                var result = await AsyncPhantasma.FromApi<bool>(
                                    (onSuccess, onError) => phantasmaApi.WriteArchive(hash.ToString(), blockIndex, data, onSuccess, onError),
                                    CancellationToken.None);
                                Log.Write("WriteArchive result: " + result, Log.Level.Debug1);
                                callback(result, null);
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);
                                callback(false, ex.Message);
                            }
                            break;
                        }
                    default:
                        {
                            callback(false, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        // We use this to detect when account was just loaded
        // and needs balances/histories to be loaded.
        public void RequestConfirmation(string transactionHash, int checkCount, Action<TransactionResult, string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        try
                        {
                            var txResult = await AsyncPhantasma.FromApi<TransactionResult>(
                                (onSuccess, onError) => phantasmaApi.GetTransaction(
                                    transactionHash,
                                    onSuccess,
                                    onError,
                                    timeout: WebClient.DefaultTimeout,
                                    retries: NetworkRetryPolicy.Retries),
                                CancellationToken.None);

                            if (txResult.State == ExecutionState.Running)
                            {
                                callback(txResult, "pending");
                            }
                            else if (txResult.State == ExecutionState.Break || txResult.State == ExecutionState.Fault)
                            {
                                if (string.IsNullOrEmpty(txResult.DebugComment) && checkCount <= 6)
                                {
                                    // We wait a bit for additional information about failure to become available
                                    callback(txResult, "pending");
                                }
                                else
                                {
                                    callback(txResult, "Transaction failed");
                                }
                            }
                            else
                            {
                                callback(txResult, null);
                            }
                        }
                        catch (PhantasmaRequestException ex)
                        {
                            RotateRpcOnWebError(ex);

                            var msg = ex.Message;
                            if (checkCount <= maxChecks)
                            {
                                if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.FAILED_PARSING_JSON)
                                {
                                    msg = "Cannot determine if transaction was successful or not due to incorrect RPC response. " + msg;
                                }
                                else if (msg.ToUpperInvariant().Contains("PENDING") || msg.ToUpperInvariant().Contains("TRANSACTION NOT FOUND"))
                                {
                                    // If tx is PENDING or NOT FOUND, we want to wait till timeout
                                    // to ensure that no new information about tx will appear.
                                    msg = "pending";
                                }
                                callback(null, msg);
                            }
                            else
                            {
                                callback(null, "timeout");
                            }
                        }

                        break;

                    default:
                        callback(null, "not implemented: " + CurrentPlatform);
                        break;
                }
            }

            ExecuteAsync().Forget(LogTaskException);

        }

    }
}
