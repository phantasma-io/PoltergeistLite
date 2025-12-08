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

        public static Task<T> PostAsync<T>(string url, string body, CancellationToken cancellationToken = default)
        {
            return AsyncPhantasma.FromApi<T>((onSuccess, onError) => WebClient.RESTPost<T>(url, body, onError, onSuccess), cancellationToken);
        }
    }
}
