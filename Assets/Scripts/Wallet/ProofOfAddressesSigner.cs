using Phantasma.Core.Cryptography;
using Phantasma.Core.Cryptography.ECDsa;
using Poltergeist.PhantasmaLegacy.Ethereum;
using Poltergeist.Neo2.Core;
using Phantasma.Core.Cryptography.EdDSA;

public class ProofOfAddressesSigner
{
    private readonly PhantasmaKeys PhantasmaKeys;
    private readonly EthereumKey EthKeys;
    private readonly NeoKeys NeoKeys;

    public ProofOfAddressesSigner(string wif)
    {
        PhantasmaKeys = PhantasmaKeys.FromWIF(wif);
        EthKeys = EthereumKey.FromWIF(wif);
        NeoKeys = NeoKeys.FromWIF(wif);
    }

    public string GenerateMessage()
    {
        var phaAddress = PhantasmaKeys.Address.ToString();
        var ethAddress = new Poltergeist.PhantasmaLegacy.Ethereum.Util.AddressUtil().ConvertToChecksumAddress(EthereumKey.PublicKeyToAddress(EthKeys.UncompressedPublicKey));
        var ethPubKey = System.Convert.ToBase64String(EthKeys.UncompressedPublicKey);

        var neo2Address = Poltergeist.Neo2.Core.NeoKeys.PublicKeyToN2Address(NeoKeys.PublicKey);
        var neo2PubKey = System.Convert.ToBase64String(NeoKeys.PublicKey);


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
        message += "\n\nPhantasma signature: " + System.Convert.ToBase64String(((Ed25519Signature)phaSignature).Bytes);

        {
            var signatureBytes = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Sign(messageBytes, EthKeys.PrivateKey, EthKeys.PublicKey, ECDsaCurve.Secp256k1);
            message += "\nEthereum signature: " + System.Convert.ToBase64String(signatureBytes);
        }

        {
            var signatureBytes = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Sign(messageBytes, NeoKeys.PrivateKey, NeoKeys.CompressedPublicKey, ECDsaCurve.Secp256r1);
            message += "\nNeo Legacy signature: " + System.Convert.ToBase64String(signatureBytes);
        }

        return message;
    }
}

