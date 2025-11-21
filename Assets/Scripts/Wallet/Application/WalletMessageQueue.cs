namespace Poltergeist.Wallet
{
    /// <summary>
    /// Represents a user-facing message that needs to be displayed by the UI.
    /// </summary>
    public readonly struct WalletUserMessage
    {
        public WalletUserMessage(string title, string body, MessageKind kind)
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Message" : title;
            Body = body ?? string.Empty;
            Kind = kind;
        }

        public string Title { get; }

        public string Body { get; }

        public MessageKind Kind { get; }
    }

    /// <summary>
    /// Simple queue that stores the next message that must be shown to the player.
    /// </summary>
    public sealed class WalletMessageQueue
    {
        private WalletUserMessage? _pendingMessage;

        public bool HasPending => _pendingMessage.HasValue;

        public void Push(string body, string title = "Warning", MessageKind kind = MessageKind.Default)
        {
            _pendingMessage = new WalletUserMessage(title, body, kind);
        }

        public bool TryDequeue(out WalletUserMessage message)
        {
            if (_pendingMessage.HasValue)
            {
                message = _pendingMessage.Value;
                _pendingMessage = null;
                return true;
            }

            message = default;
            return false;
        }

        public void Clear()
        {
            _pendingMessage = null;
        }
    }
}
