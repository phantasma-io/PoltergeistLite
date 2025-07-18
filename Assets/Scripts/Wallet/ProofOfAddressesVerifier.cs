using System.Linq;
using System.Text;
using PhantasmaPhoenix.Cryptography;

public class ProofOfAddressesVerifier
{
    public readonly string Message;
    public readonly string SignedMessage;
    private readonly byte[] SignedMessageBytes;

    public readonly string PhaAddress;
    private readonly byte[] PhaPublicKeyBytes;

    public readonly string EthAddress;
    public readonly string EthPublicKey;
    private readonly byte[] EthPublicKeyBytes;

    public readonly string Neo2Address;
    public readonly string Neo2PublicKey;
    private readonly byte[] Neo2PublicKeyBytes;

    public readonly string PhaSignature;
    private readonly byte[] PhaSignatureBytes;
    public readonly string EthSignature;
    private readonly byte[] EthSignatureBytes;
    public readonly string Neo2Signature;
    private readonly byte[] Neo2SignatureBytes;

    public ProofOfAddressesVerifier(string message)
    {
        Message = message;

        var split = Message.Replace("\r", string.Empty).Split('\n');

        SignedMessage = string.Join('\n', split.Take(6));
        SignedMessageBytes = Encoding.ASCII.GetBytes(SignedMessage);

        PhaAddress = split[1].Substring(19);
        PhaPublicKeyBytes = Address.Parse(PhaAddress).GetPublicKey();
        EthAddress = split[2].Substring(18);
        EthPublicKey = split[3].Substring(21);
        EthPublicKeyBytes = Base16.Decode(EthPublicKey);
        Neo2Address = split[4].Substring(20);
        Neo2PublicKey = split[5].Substring(23);
        Neo2PublicKeyBytes = Base16.Decode(Neo2PublicKey);

        PhaSignature = split[7].Substring(21);
        PhaSignatureBytes = Base16.Decode(PhaSignature);
        EthSignature = split[8].Substring(20);
        EthSignatureBytes = Base16.Decode(EthSignature);
        Neo2Signature = split[9].Substring(22);
        Neo2SignatureBytes = Base16.Decode(Neo2Signature);
    }

    public (bool, string) VerifyMessage()
    {
        bool success = true;
        string errorMessage = "";

        if (!Ed25519.Verify(PhaSignatureBytes, SignedMessageBytes, PhaPublicKeyBytes))
        {
            success = false;
            errorMessage += "Phantasma signature is incorrect!\n";
        }
        if (!ECDsa.Verify(SignedMessageBytes, EthSignatureBytes, EthPublicKeyBytes, ECDsaCurve.Secp256k1))
        {
            success = false;
            errorMessage += "Ethereum signature is incorrect!\n";
        }
        if (!ECDsa.Verify(SignedMessageBytes, Neo2SignatureBytes, Neo2PublicKeyBytes, ECDsaCurve.Secp256r1))
        {
            success = false;
            errorMessage += "Neo Legacy signature is incorrect!\n";
        }

        var ethAddressFromPublicKey = new PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util.AddressUtil().ConvertToChecksumAddress(PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.PublicKeyToAddress(EthPublicKeyBytes, ECDsaCurve.Secp256k1));
        if (EthAddress != ethAddressFromPublicKey)
        {
            success = false;
            errorMessage += "Ethereum address is incorrect: " + ethAddressFromPublicKey + "\n";
        }

        var neo2AddressFromPublicKey = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.PublicKeyToN2Address(Neo2PublicKeyBytes);
        if (Neo2Address != neo2AddressFromPublicKey)
        {
            success = false;
            errorMessage += "Neo Legacy address is incorrect: " + neo2AddressFromPublicKey + "\n";
        }

        return (success, errorMessage);
    }
}

