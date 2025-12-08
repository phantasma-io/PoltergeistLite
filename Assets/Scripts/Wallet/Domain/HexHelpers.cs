using System.Linq;

public static class HexUtils
{
    public static string CleanHex(this string hexText)
    {
        if (string.IsNullOrWhiteSpace(hexText)) return string.Empty;
        return new string(hexText.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }
}
