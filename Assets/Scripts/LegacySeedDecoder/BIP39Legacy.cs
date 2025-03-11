using Phantasma.Core.Cryptography;
using System.Linq;

public static class BIP39Legacy
{
    public static string DecodeLegacySeedToWif(string mnemonicPhrase, string password)
    {
        var privKey = BIP39Legacy.MnemonicToPK(mnemonicPhrase, password);
        var decryptedKeys = new PhantasmaKeys(privKey);
        return decryptedKeys.ToWIF();
    }

    private static byte[] MnemonicToPK(string mnemonicPhrase, string password)
    {
        var bip = new Bitcoin.BIP39.BIP39(mnemonicPhrase, password);
        return bip.SeedBytes.Take(32).ToArray();
    }
}
