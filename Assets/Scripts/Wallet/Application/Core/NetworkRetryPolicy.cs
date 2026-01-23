using System;

namespace Poltergeist.Wallet
{
    internal static class NetworkRetryPolicy
    {
        // Shared retry settings for safe network operations (read-only calls and re-broadcasts of identical signed bytes).
        public const int MaxAttempts = 3;
        public const int RetryDelaySeconds = 1;

        public static int Retries => Math.Max(0, MaxAttempts - 1);
        public static TimeSpan RetryDelay => TimeSpan.FromSeconds(RetryDelaySeconds);
    }
}
