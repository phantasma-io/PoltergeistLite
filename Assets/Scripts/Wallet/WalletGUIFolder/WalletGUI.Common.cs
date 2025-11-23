namespace Poltergeist
{
    public struct MenuEntry
    {
        public readonly object value;
        public readonly string label;
        public readonly bool enabled;

        public MenuEntry(object value, string label, bool enabled)
        {
            this.value = value;
            this.label = label;
            this.enabled = enabled;
        }
    }

    public enum AnimationDirection
    {
        None,
        Up,
        Down,
        Left,
        Right
    }
}
