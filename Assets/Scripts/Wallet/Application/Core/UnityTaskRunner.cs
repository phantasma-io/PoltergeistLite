using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Runs coroutines and marshals Task completions back onto the Unity main thread.
    /// Bridges coroutine world (SDK callbacks) with Task-based async used in the application layer.
    /// </summary>
    internal sealed class UnityTaskRunner : MonoBehaviour
    {
        private static UnityTaskRunner _instance;
        private static SynchronizationContext _unityContext;
        private static int _unityThreadId = -1;

        private static UnityTaskRunner Instance => _instance ?? CreateInstance();

        internal static SynchronizationContext UnityContext => _unityContext;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Ensure the runner is available as soon as the player starts.
            _ = Instance;
        }

        private static UnityTaskRunner CreateInstance()
        {
            var existing = FindExistingInstance();
            if (existing != null)
            {
                _instance = existing;
                CaptureUnityContext();
                return _instance;
            }

            var go = new GameObject(nameof(UnityTaskRunner));
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<UnityTaskRunner>();
            CaptureUnityContext();
            return _instance;
        }

        private static UnityTaskRunner FindExistingInstance()
        {
            return FindFirstObjectByType<UnityTaskRunner>();
        }

        private static void CaptureUnityContext()
        {
            var current = SynchronizationContext.Current;
            if (current != null)
            {
                _unityContext = current;
                _unityThreadId = Environment.CurrentManagedThreadId;
            }
            else if (_unityContext == null)
            {
                _unityContext = new SynchronizationContext();
                _unityThreadId = Environment.CurrentManagedThreadId;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            CaptureUnityContext();
        }

        internal static void PostToMainThread(Action action)
        {
            var context = _unityContext ?? SynchronizationContext.Current;
            if (context == null)
            {
                action();
                return;
            }

            // If already on the Unity thread/context, execute immediately to avoid deadlocks.
            if (ReferenceEquals(context, SynchronizationContext.Current) ||
                (_unityThreadId != -1 && Environment.CurrentManagedThreadId == _unityThreadId))
            {
                action();
                return;
            }

            context.Post(_ => action(), null);
        }

        internal static Task RunCoroutineAsync(Func<IEnumerator> coroutineFactory, CancellationToken cancellationToken = default)
        {
            return RunCoroutineAsync<object>(_ =>
            {
                return coroutineFactory();
            }, cancellationToken);
        }

        internal static Task<TResult> RunCoroutineAsync<TResult>(Func<TaskCompletionSource<TResult>, IEnumerator> coroutineFactory, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Coroutine routine = null;
            IDisposable cancellationRegistration = null;
            void DisposeCancellation() => cancellationRegistration?.Dispose();

            void Cancel()
            {
                PostToMainThread(() =>
                {
                    if (routine != null)
                    {
                        Instance.StopCoroutine(routine);
                    }

                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(Cancel);
            }

            IEnumerator Wrapper()
            {
                // Manually drive inner coroutine to catch exceptions and ensure Task completion paths.
                IEnumerator innerCoroutine;
                try
                {
                    innerCoroutine = coroutineFactory(tcs);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    DisposeCancellation();
                    yield break;
                }

                while (true)
                {
                    bool moveNext;
                    try
                    {
                        moveNext = innerCoroutine.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                        DisposeCancellation();
                        yield break;
                    }

                    if (!moveNext)
                    {
                        break;
                    }

                    yield return innerCoroutine.Current;
                }

                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetException(new InvalidOperationException("Coroutine finished without completing the task."));
                }

                DisposeCancellation();
            }

            routine = Instance.StartCoroutine(Wrapper());
            return tcs.Task;
        }
    }
}
