using System;
using System.Threading.Tasks;

namespace Poltergeist.Wallet
{
    internal static class TaskExtensions
    {
        // Fire-and-forget helper: runs a Task without awaiting but surfaces exceptions via callback/log.
        public static void Forget(this Task task, Action<Exception> onError = null)
        {
            task.ContinueWith(t =>
            {
                onError?.Invoke(t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
