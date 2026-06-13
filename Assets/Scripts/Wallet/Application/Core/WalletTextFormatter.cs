using UnityEngine;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Formats long opaque strings (addresses, hashes) for display in a consistent way across UI layers.
    /// </summary>
    public static class WalletTextFormatter
    {
        /// <summary>
        /// Collapses the middle of a long value into an ellipsis, keeping <paramref name="head"/> leading and
        /// <paramref name="tail"/> trailing characters. Returns the value unchanged when it is already short.
        /// </summary>
        public static string AbbreviateMiddle(string value, int head = 4, int tail = 4)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length <= head + tail + 3)
            {
                return value;
            }

            var prefix = value.Substring(0, head);
            var suffix = value.Substring(value.Length - tail, tail);
            return $"{prefix}...{suffix}";
        }

        /// <summary>
        /// Like <see cref="AbbreviateMiddle"/>, but only shortens on small mobile screens; on desktop, where
        /// there is room, the full value is returned. Use for display strings that should stay full on wide
        /// screens and shrink to fit narrow ones.
        /// </summary>
        public static string AbbreviateMiddleForDisplay(string value, int head = 4, int tail = 4)
        {
            return Application.isMobilePlatform ? AbbreviateMiddle(value, head, tail) : (value ?? string.Empty);
        }
    }
}
