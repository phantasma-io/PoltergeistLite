using System;
using System.Linq;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Poltergeist.PhantasmaLegacy.Ethereum.Hex.HexConvertors.Extensions;

namespace Phantasma.Core.Cryptography.ECDsa
{
    public enum ECDsaCurve
    {
        Secp256r1,
        Secp256k1,
    }

    public static class ECDsa
    {
        public static byte[] GetPublicKey(byte[] privateKey, bool compressed, ECDsaCurve curve)
        {
            X9ECParameters ecCurve;
            switch (curve)
            {
                case ECDsaCurve.Secp256k1:
                    ecCurve = SecNamedCurves.GetByName("secp256k1");
                    break;
                default:
                    ecCurve = SecNamedCurves.GetByName("secp256r1");
                    break;
            }

            var dom = new ECDomainParameters(ecCurve.Curve, ecCurve.G, ecCurve.N, ecCurve.H);

            var d = new BigInteger(1, privateKey);
            var q = dom.G.Multiply(d);


            var publicParams = new ECPublicKeyParameters(q, dom);
            return publicParams.Q.GetEncoded(compressed);
        }

        public static byte[] CompressPublicKey(byte[] uncompressedPublicKey)
        {
            var x = new BigInteger(1, uncompressedPublicKey.Take(32).ToArray());
            var y = new BigInteger(1, uncompressedPublicKey.Skip(32).ToArray());

            byte prefix = 0x02;
            if (y.Mod(BigInteger.Two) != BigInteger.Zero)
            {
                prefix = 0x03;
            }

            return new byte[] { prefix }.Concat(x.ToByteArrayUnsigned()).ToArray();
        }

        public static byte[] DecompressPublicKey(byte[] compressedPublicKey, ECDsaCurve curve, bool dropUncompressedKeyPrefixByte = false)
        {
            if (compressedPublicKey.Length != 33)
            {
                throw new ArgumentException("Incorrect compressed key length: " + compressedPublicKey.Length);
            }

            X9ECParameters ecCurve;
            switch (curve)
            {
                case ECDsaCurve.Secp256k1:
                    ecCurve = SecNamedCurves.GetByName("secp256k1");
                    break;
                default:
                    ecCurve = SecNamedCurves.GetByName("secp256r1");
                    break;
            }
            var dom = new ECDomainParameters(ecCurve.Curve, ecCurve.G, ecCurve.N, ecCurve.H);

            ECPublicKeyParameters publicKeyParameters = new ECPublicKeyParameters(dom.Curve.DecodePoint(compressedPublicKey), dom);
            var uncompressedPublicKey = publicKeyParameters.Q.GetEncoded(false);

            if (dropUncompressedKeyPrefixByte)
            {
                uncompressedPublicKey = uncompressedPublicKey.Skip(1).ToArray();
            }

            return uncompressedPublicKey;
        }

        public static byte[] Sign(byte[] message, byte[] prikey, ECDsaCurve curve)
        {
            var signer = SignerUtilities.GetSigner("SHA256withECDSA");
            X9ECParameters ecCurve;
            switch (curve)
            {
                case ECDsaCurve.Secp256k1:
                    ecCurve = SecNamedCurves.GetByName("secp256k1");
                    break;
                default:
                    ecCurve = SecNamedCurves.GetByName("secp256r1");
                    break;
            }
            var dom = new ECDomainParameters(ecCurve.Curve, ecCurve.G, ecCurve.N, ecCurve.H);
            var privateKeyParameters = new ECPrivateKeyParameters(new BigInteger(1, prikey), dom);

            signer.Init(true, privateKeyParameters);
            signer.BlockUpdate(message, 0, message.Length);
            var signature = signer.GenerateSignature();

            return ECDsaHelpers.FromDER(signature);
        }

        public static bool Verify(byte[] message, byte[] signature, byte[] pubkey, ECDsaCurve curve)
        {
            var signer = SignerUtilities.GetSigner("SHA256withECDSA");
            X9ECParameters ecCurve;
            switch (curve)
            {
                case ECDsaCurve.Secp256k1:
                    ecCurve = SecNamedCurves.GetByName("secp256k1");
                    break;
                default:
                    ecCurve = SecNamedCurves.GetByName("secp256r1");
                    break;
            }
            var dom = new ECDomainParameters(ecCurve.Curve, ecCurve.G, ecCurve.N, ecCurve.H);

            ECPublicKeyParameters publicKeyParameters;
            if (pubkey.Length == 33)
                publicKeyParameters = new ECPublicKeyParameters(dom.Curve.DecodePoint(pubkey), dom);
            else
                publicKeyParameters = new ECPublicKeyParameters(dom.Curve.CreatePoint(new BigInteger(1, pubkey.Take(pubkey.Length / 2).ToArray()), new BigInteger(1, pubkey.Skip(pubkey.Length / 2).ToArray())), dom);

            signer.Init(false, publicKeyParameters);
            signer.BlockUpdate(message, 0, message.Length);

            return signer.VerifySignature(ECDsaHelpers.ToDER(signature));
        }
    }
}
