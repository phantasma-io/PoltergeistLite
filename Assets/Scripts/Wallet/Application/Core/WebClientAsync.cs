using System;
using System.Threading;
using System.Threading.Tasks;
using PhantasmaPhoenix.Unity.Core;

namespace Poltergeist.Wallet
{
    internal static class WebClientAsync
    {
        // Task-friendly wrappers over REST helpers routed through AsyncPhantasma for consistent threading/error handling.
        public static Task<T> GetAsync<T>(string url, int timeout, CancellationToken cancellationToken = default)
        {
            return AsyncPhantasma.FromApi<T>((onSuccess, onError) => WebClient.RESTGet<T>(url, timeout, onError, onSuccess), cancellationToken);
        }

        public static Task<T> GetAsync<T>(string url, int timeout, int retries, TimeSpan retryDelay, CancellationToken cancellationToken = default)
        {
            return ExecuteWithRetriesAsync(
                () => AsyncPhantasma.FromApi<T>((onSuccess, onError) => WebClient.RESTGet<T>(url, timeout, onError, onSuccess), cancellationToken),
                retries,
                retryDelay,
                cancellationToken);
        }

        public static Task<T> PostAsync<T>(string url, string body, CancellationToken cancellationToken = default)
        {
            return AsyncPhantasma.FromApi<T>((onSuccess, onError) => WebClient.RESTPost<T>(url, body, onError, onSuccess), cancellationToken);
        }

        public static Task<T> PostAsync<T>(string url, string body, int retries, TimeSpan retryDelay, CancellationToken cancellationToken = default)
        {
            return ExecuteWithRetriesAsync(
                () => AsyncPhantasma.FromApi<T>((onSuccess, onError) => WebClient.RESTPost<T>(url, body, onError, onSuccess), cancellationToken),
                retries,
                retryDelay,
                cancellationToken);
        }

        // Retry only on transport failures; application/API errors should surface immediately.
        private static async Task<T> ExecuteWithRetriesAsync<T>(Func<Task<T>> operation, int retries, TimeSpan retryDelay, CancellationToken cancellationToken)
        {
            var remaining = Math.Max(0, retries);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation();
                }
                catch (PhantasmaRequestException ex) when (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR && remaining > 0)
                {
                    remaining--;
                    if (retryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(retryDelay, cancellationToken);
                    }
                }
            }
        }
    }
}
