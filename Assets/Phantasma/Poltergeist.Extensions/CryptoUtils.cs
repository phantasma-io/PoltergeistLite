using System;
using System.Linq;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Phantasma.Core.Cryptography.ECDsa;

namespace Poltergeist.PhantasmaLegacy.Cryptography
{
    public static class CryptoUtils
    {
        public enum SignatureFormat
        {
            None,
            Concatenated,
            DEREncoded
        }

        public static byte[] Sign(byte[] message, byte[] prikey, ECDsaCurve curve, SignatureFormat signatureFormat = SignatureFormat.Concatenated)
        {
            var signer = SignerUtilities.GetSigner("SHA256withECDSA");
            var privateKeyParameters = ECDsaHelpers.GetECPrivateKeyParameters(curve, prikey);

            signer.Init(true, privateKeyParameters);
            signer.BlockUpdate(message, 0, message.Length);
            var sig = signer.GenerateSignature();

            switch (signatureFormat)
            {
                case SignatureFormat.Concatenated:
                    // We convert from default DER format that Bouncy Castle uses to concatenated "raw" R + S format.
                    return ECDsaHelpers.FromDER(sig);
                case SignatureFormat.DEREncoded:
                    // Return DER-encoded signature unchanged.
                    return sig;
                default:
                    throw new Exception("Unknown signature format");
            }
        }

        public static byte[] SignDeterministic(byte[] message, byte[] prikey, ECDsaCurve curve)
        {
            var messageHash = Sha256Hash(message);

            var privateKeyParameters = ECDsaHelpers.GetECPrivateKeyParameters(curve, prikey);

            var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));

            signer.Init(true, privateKeyParameters);

            var RS = signer.GenerateSignature(messageHash);
            var R = RS[0].ToByteArrayUnsigned();
            var S = RS[1].ToByteArrayUnsigned();

            return R.Concat(S).ToArray();
        }

        public static bool Verify(byte[] message, byte[] signature, byte[] pubkey, ECDsaCurve curve, SignatureFormat signatureFormat = SignatureFormat.Concatenated)
        {
            var signer = SignerUtilities.GetSigner("SHA256withECDSA");
            var publicKeyParameters = ECDsaHelpers.GetECPublicKeyParameters(curve, pubkey);

            signer.Init(false, publicKeyParameters);
            signer.BlockUpdate(message, 0, message.Length);

            switch (signatureFormat)
            {
                case SignatureFormat.Concatenated:
                    // We convert from concatenated "raw" R + S format to DER format that Bouncy Castle uses.
                    signature = ECDsaHelpers.ToDER(signature);
                    break;
                case SignatureFormat.DEREncoded:
                    // Do nothing, signature already DER-encoded.
                    break;
                default:
                    throw new Exception("Unknown signature format");
            }

            return signer.VerifySignature(signature);
        }

        public static byte[] Sha256Hash(byte[] message)
        {
            var sha256 = new Phantasma.Core.Cryptography.Hashing.SHA256();
            return sha256.ComputeHash(message);
        }
    }
}
