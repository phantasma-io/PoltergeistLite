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
        private string GetFastestWorkingRPCURL(out TimeSpan responseTime)
        {
            string fastestRpcUrl = null;

            responseTime = TimeSpan.Zero;

            foreach (var rpcResponseTime in rpcResponseTimesPhantasma)
            {
                if (!rpcResponseTime.ConnectionError && String.IsNullOrEmpty(fastestRpcUrl))
                {
                    // At first just initializing with first working RPC.
                    fastestRpcUrl = rpcResponseTime.Url;
                    responseTime = rpcResponseTime.ResponseTime;
                }
                else if (!rpcResponseTime.ConnectionError && rpcResponseTime.ResponseTime < responseTime)
                {
                    // Faster RPC found, switching.
                    fastestRpcUrl = rpcResponseTime.Url;
                    responseTime = rpcResponseTime.ResponseTime;
                }
            }
            return fastestRpcUrl;
        }

        public void UpdateRPCURL()
        {
            async Task ExecuteAsync()
            {
                if (Settings.nexusKind == NexusKind.Dev_Net)
                {
                    rpcAvailablePhantasma = 1;
                    return;
                }

                if (Settings.nexusKind != NexusKind.Main_Net && Settings.nexusKind != NexusKind.Test_Net)
                {
                    rpcAvailablePhantasma = 1;
                    return; // No need to change RPC, it is set by custom settings.
                }

                string url = Settings.nexusKind == NexusKind.Main_Net
                    ? "https://peers.phantasma.info/mainnet-getpeers.json"
                    : "https://peers.phantasma.info/testnet-getpeers.json";

                rpcBenchmarkedPhantasma = 0;
                rpcResponseTimesPhantasma = new List<RpcBenchmarkData>();

                try
                {
                    var response = await WebClientAsync.GetAsync<JToken>(
                        url,
                        WebClient.DefaultTimeout,
                        NetworkRetryPolicy.Retries,
                        NetworkRetryPolicy.RetryDelay,
                        CancellationToken.None);
                    if (response != null)
                    {
                        rpcNumberPhantasma = response.Count();

                        if (String.IsNullOrEmpty(Settings.phantasmaRPCURL))
                        {
                            var index = ((int)(Time.realtimeSinceStartup * 1000)) % rpcNumberPhantasma;
                            var node = response[index];
                            var result = node.Value<string>("url") + "/rpc";
                            Settings.phantasmaRPCURL = result;
                            Log.Write($"Changed Phantasma RPC url {index} => {result}");
                        }

                        UpdateAPIs();

                        var benchmarkTasks = new List<Task>();
                        foreach (var node in response.Children())
                        {
                            var rpcUrl = node.Value<string>("url") + "/rpc";
                            benchmarkTasks.Add(BenchmarkRpcAsync(rpcUrl));
                        }

                        await Task.WhenAll(benchmarkTasks);

                        if (rpcBenchmarkedPhantasma == rpcNumberPhantasma)
                        {
                            TimeSpan bestTime;
                            string bestRpcUrl = GetFastestWorkingRPCURL(out bestTime);

                            if (String.IsNullOrEmpty(bestRpcUrl))
                            {
                                ReportAllRpcsUnavailabe = true;
                                Log.WriteWarning("All Phantasma RPC servers are unavailable. Please check your network connection.");
                            }
                            else
                            {
                                Log.Write($"Fastest Phantasma RPC is {bestRpcUrl}: {new DateTime(bestTime.Ticks).ToString("ss.fff")} sec.");
                                Settings.phantasmaRPCURL = bestRpcUrl;
                                UpdateAPIs();
                                Settings.SaveOnExit();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ReportGetPeersFailure = true;
                    Log.Write($"Couldn't retrieve RPCs list using url '{url}', error: " + ex.Message);
                }
            }

            // Kick off the async refresh without recursive calls to avoid starving the thread pool.
            ExecuteAsync().Forget(LogTaskException);
        }

        private async Task BenchmarkRpcAsync(string rpcUrl)
        {
            // Benchmark should include retry delays so slow/flaky endpoints score worse.
            var startedAt = DateTime.UtcNow;
            var remainingRetries = NetworkRetryPolicy.Retries;

            try
            {
                while (true)
                {
                    try
                    {
                        await AsyncPhantasma.FromApi<TimeSpan>(
                            (onSuccess, onError) => WebClient.Ping(rpcUrl, onError, onSuccess),
                            CancellationToken.None);

                        var totalTime = DateTime.UtcNow - startedAt;
                        lock (rpcResponseTimesPhantasma)
                        {
                            rpcResponseTimesPhantasma.Add(new RpcBenchmarkData(rpcUrl, false, totalTime));
                        }

                        Interlocked.Increment(ref rpcAvailablePhantasma);
                        break;
                    }
                    catch (PhantasmaRequestException ex) when (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR && remainingRetries > 0)
                    {
                        remainingRetries--;
                        if (NetworkRetryPolicy.RetryDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(NetworkRetryPolicy.RetryDelay);
                        }
                    }
                }
            }
            catch (PhantasmaRequestException ex)
            {
                Log.Write("Ping error: " + ex.Message);

                lock (rpcResponseTimesPhantasma)
                {
                    rpcResponseTimesPhantasma.Add(new RpcBenchmarkData(rpcUrl, true, new TimeSpan()));
                }
            }
            finally
            {
                Interlocked.Increment(ref rpcBenchmarkedPhantasma);
            }
        }
        private void RotateRpcOnWebError(PhantasmaRequestException ex)
        {
            if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
            {
                ChangeFaultyRPCURL(PlatformKind.Phantasma);
            }
        }

        public void ChangeFaultyRPCURL(PlatformKind platformKind)
        {
            if (Settings.nexusKind != NexusKind.Main_Net ||
                (platformKind == PlatformKind.BSC && Settings.nexusKind != NexusKind.Main_Net && Settings.nexusKind != NexusKind.Test_Net))
            {
                return; // Fallback works only for mainnet or BSC testnet.
            }

            if (platformKind == PlatformKind.Phantasma)
            {
                Log.Write($"Changing faulty Phantasma RPC {Settings.phantasmaRPCURL}.");

                // Now we have one less working RPC.
                if (rpcAvailablePhantasma > 0)
                    rpcAvailablePhantasma--;

                // Marking faulty RPC.
                var currentRpc = rpcResponseTimesPhantasma.Find(x => x.Url == Settings.phantasmaRPCURL);
                if (currentRpc != null)
                    currentRpc.ConnectionError = true;

                // Switching to working RPC.
                TimeSpan bestTime;
                string bestRpcUrl = GetFastestWorkingRPCURL(out bestTime);

                if (String.IsNullOrEmpty(bestRpcUrl))
                {
                    ReportAllRpcsUnavailabe = true;
                    Log.WriteWarning("All Phantasma RPC servers are unavailable. Please check your network connection.");
                }
                else
                {
                    Log.Write($"Next fastest Phantasma RPC is {bestRpcUrl}: {new DateTime(bestTime.Ticks).ToString("ss.fff")} sec.");
                    Settings.phantasmaRPCURL = bestRpcUrl;
                    UpdateAPIs();
                }
            }
        }
    }
}
