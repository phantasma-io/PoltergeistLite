using System;
using System.Linq;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
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

        // Transcodes the JCA ASN.1/DER-encoded signature into the concatenated R + S format expected by ECDSA JWS.
        public static byte[] TranscodeSignatureToConcat(byte[] derSignature, int outputLength)
        {
            if (derSignature.Length < 8 || derSignature[0] != 48) throw new Exception("Invalid ECDSA signature format");

            int offset;
            if (derSignature[1] > 0)
                offset = 2;
            else if (derSignature[1] == 0x81)
                offset = 3;
            else
                throw new Exception("Invalid ECDSA signature format");

            var rLength = derSignature[offset + 1];

            int i = rLength;
            while (i > 0
                   && derSignature[offset + 2 + rLength - i] == 0)
                i--;

            var sLength = derSignature[offset + 2 + rLength + 1];

            int j = sLength;
            while (j > 0
                   && derSignature[offset + 2 + rLength + 2 + sLength - j] == 0)
                j--;

            var rawLen = Math.Max(i, j);
            rawLen = Math.Max(rawLen, outputLength / 2);

            if ((derSignature[offset - 1] & 0xff) != derSignature.Length - offset
                || (derSignature[offset - 1] & 0xff) != 2 + rLength + 2 + sLength
                || derSignature[offset] != 2
                || derSignature[offset + 2 + rLength] != 2)
                throw new Exception("Invalid ECDSA signature format");

            var concatSignature = new byte[2 * rawLen];

            Array.Copy(derSignature, offset + 2 + rLength - i, concatSignature, rawLen - i, i);
            Array.Copy(derSignature, offset + 2 + rLength + 2 + sLength - j, concatSignature, 2 * rawLen - j, j);

            return concatSignature;
        }

        public static byte[] RSBytesToDER(byte[] RSBytes)
        {
            return new Org.BouncyCastle.Asn1.DerSequence(
                        // first 32 bytes is "r" number
                        new Org.BouncyCastle.Asn1.DerInteger(new BigInteger(1, RSBytes.Take(32).ToArray())),
                        // last 32 bytes is "s" number
                        new Org.BouncyCastle.Asn1.DerInteger(new BigInteger(1, RSBytes.Skip(32).ToArray())))
                        .GetDerEncoded();
        }

        private static X9ECParameters GetECParameters(ECDsaCurve curve)
        {
            return curve switch
            {
                ECDsaCurve.Secp256k1 => ECNamedCurveTable.GetByName("secp256k1"),
                ECDsaCurve.Secp256r1 => ECNamedCurveTable.GetByName("secp256r1"),
                _ => ECNamedCurveTable.GetByName("secp256r1"),
            };
        }

        private static ECDomainParameters GetECDomainParameters(ECDsaCurve curve)
        {
            var ecParams = GetECParameters(curve);
            return new ECDomainParameters(ecParams.Curve, ecParams.G, ecParams.N, ecParams.H);
        }

        private static ECPrivateKeyParameters GetECPrivateKeyParameters(ECDsaCurve curve, byte[] privateKey)
        {
            return new ECPrivateKeyParameters(new BigInteger(1, privateKey), GetECDomainParameters(curve));
        }

        private static ECPublicKeyParameters GetECPublicKeyParameters(ECDsaCurve curve, byte[] publicKey)
        {
            var ecDomainParameters = GetECDomainParameters(curve);
            var point = ecDomainParameters.Curve.DecodePoint(publicKey);
            return new ECPublicKeyParameters(point, ecDomainParameters);
        }

        public static byte[] Sign(byte[] message, byte[] prikey, ECDsaCurve curve = ECDsaCurve.Secp256r1, SignatureFormat signatureFormat = SignatureFormat.Concatenated)
        {
            var signer = SignerUtilities.GetSigner("SHA256withECDSA");
            var privateKeyParameters = GetECPrivateKeyParameters(curve, prikey);

            signer.Init(true, privateKeyParameters);
            signer.BlockUpdate(message, 0, message.Length);
            var sig = signer.GenerateSignature();

            switch (signatureFormat)
            {
                case SignatureFormat.Concatenated:
                    // We convert from default DER format that Bouncy Castle uses to concatenated "raw" R + S format.
                    return TranscodeSignatureToConcat(sig, 64);
                case SignatureFormat.DEREncoded:
                    // Return DER-encoded signature unchanged.
                    return sig;
                default:
                    throw new Exception("Unknown signature format");
            }
        }

        public static byte[] SignDeterministic(byte[] message, byte[] prikey, ECDsaCurve curve = ECDsaCurve.Secp256r1)
        {
            var messageHash = Sha256Hash(message);

            var privateKeyParameters = GetECPrivateKeyParameters(curve, prikey);

            var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));

            signer.Init(true, privateKeyParameters);

            var RS = signer.GenerateSignature(messageHash);
            var R = RS[0].ToByteArrayUnsigned();
            var S = RS[1].ToByteArrayUnsigned();

            return R.Concat(S).ToArray();
        }

        public static bool Verify(byte[] message, byte[] signature, byte[] pubkey, ECDsaCurve curve = ECDsaCurve.Secp256r1, SignatureFormat signatureFormat = SignatureFormat.Concatenated)
        {
            var signer = SignerUtilities.GetSigner("SHA256withECDSA");
            var publicKeyParameters = GetECPublicKeyParameters(curve, pubkey);

            signer.Init(false, publicKeyParameters);
            signer.BlockUpdate(message, 0, message.Length);

            switch (signatureFormat)
            {
                case SignatureFormat.Concatenated:
                    // We convert from concatenated "raw" R + S format to DER format that Bouncy Castle uses.
                    signature = RSBytesToDER(signature);
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
