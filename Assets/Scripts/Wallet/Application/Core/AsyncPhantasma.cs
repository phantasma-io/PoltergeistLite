using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using PhantasmaPhoenix.Unity.Core;

namespace Poltergeist.Wallet
{
    // Wraps Phantasma SDK coroutine callbacks into Tasks with consistent exception typing.
    internal sealed class PhantasmaRequestException : Exception
    {
        public EPHANTASMA_SDK_ERROR_TYPE ErrorType { get; }

        public PhantasmaRequestException(EPHANTASMA_SDK_ERROR_TYPE errorType, string message) : base(message)
        {
            ErrorType = errorType;
        }
    }

    internal static class AsyncPhantasma
    {
        // Converts callback-based SDK calls into Tasks while keeping execution on the Unity main thread.
        public static Task<TResult> FromApi<TResult>(Func<Action<TResult>, Action<EPHANTASMA_SDK_ERROR_TYPE, string>, IEnumerator> start, CancellationToken cancellationToken = default)
        {
            return UnityTaskRunner.RunCoroutineAsync<TResult>(tcs =>
            {
                return start(
                    result => UnityTaskRunner.PostToMainThread(() => tcs.TrySetResult(result)),
                    (error, msg) => UnityTaskRunner.PostToMainThread(() => tcs.TrySetException(new PhantasmaRequestException(error, msg)))
                );
            }, cancellationToken);
        }

        public static Task<(T1, T2)> FromApi<T1, T2>(Func<Action<T1, T2>, Action<EPHANTASMA_SDK_ERROR_TYPE, string>, IEnumerator> start, CancellationToken cancellationToken = default)
        {
            return UnityTaskRunner.RunCoroutineAsync<(T1, T2)>(tcs =>
            {
                return start(
                    (value1, value2) => UnityTaskRunner.PostToMainThread(() => tcs.TrySetResult((value1, value2))),
                    (error, msg) => UnityTaskRunner.PostToMainThread(() => tcs.TrySetException(new PhantasmaRequestException(error, msg)))
                );
            }, cancellationToken);
        }

        public static Task<(T1, T2, T3)> FromApi<T1, T2, T3>(Func<Action<T1, T2, T3>, Action<EPHANTASMA_SDK_ERROR_TYPE, string>, IEnumerator> start, CancellationToken cancellationToken = default)
        {
            return UnityTaskRunner.RunCoroutineAsync<(T1, T2, T3)>(tcs =>
            {
                return start(
                    (value1, value2, value3) => UnityTaskRunner.PostToMainThread(() => tcs.TrySetResult((value1, value2, value3))),
                    (error, msg) => UnityTaskRunner.PostToMainThread(() => tcs.TrySetException(new PhantasmaRequestException(error, msg)))
                );
            }, cancellationToken);
        }
    }
}
