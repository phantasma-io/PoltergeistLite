using PhantasmaPhoenix.Cryptography;

public class ProofOfAddressesSigner
{
    private readonly PhantasmaKeys PhantasmaKeys;
    private readonly PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey EthKeys;
    private readonly PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys NeoKeys;

    public ProofOfAddressesSigner(string wif)
    {
        PhantasmaKeys = PhantasmaKeys.FromWIF(wif);
        EthKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
        NeoKeys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
    }

    public string GenerateMessage()
    {
        var phaAddress = PhantasmaKeys.Address.ToString();
        var ethAddress = new PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util.AddressUtil().ConvertToChecksumAddress(PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.PublicKeyToAddress(EthKeys.UncompressedPublicKey, ECDsaCurve.Secp256k1));
        var ethPubKey = Base16.Encode(EthKeys.CompressedPublicKey);

        var neo2Address = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.PublicKeyToN2Address(NeoKeys.PublicKey);
        var neo2PubKey = Base16.Encode(NeoKeys.CompressedPublicKey);


        var message = "I have signed this message with my Phantasma, Ethereum and Neo Legacy signatures to prove that following addresses belong to me and were derived from private key that belongs to me and to confirm my willingness to swap funds across these addresses upon my request. My public addresses are:\n" +
            "Phantasma address: " + phaAddress + "\n" +
            "Ethereum address: " + ethAddress + "\n" +
            "Ethereum public key: " + ethPubKey + "\n" +
            "Neo Legacy address: " + neo2Address + "\n" +
            "Neo Legacy public key: " + neo2PubKey;

        return message;
    }

    public string GenerateSignedMessage()
    {
        var message = GenerateMessage();

        var messageBytes = System.Text.Encoding.ASCII.GetBytes(message);

        var phaSignature = PhantasmaKeys.Sign(messageBytes);
        message += "\n\nPhantasma signature: " + Base16.Encode(((Ed25519Signature)phaSignature).Bytes);

        {
            var signatureBytes = ECDsa.SignDeterministic(messageBytes, EthKeys.PrivateKey, ECDsaCurve.Secp256k1);
            message += "\nEthereum signature: " + Base16.Encode(signatureBytes);
        }

        {
            var signatureBytes = ECDsa.SignDeterministic(messageBytes, NeoKeys.PrivateKey, ECDsaCurve.Secp256r1);
            message += "\nNeo Legacy signature: " + Base16.Encode(signatureBytes);
        }

        return message;
    }
}

