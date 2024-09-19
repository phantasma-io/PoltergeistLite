using System;
using System.Linq;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace Phantasma.Core.Cryptography.ECDsa
{
    public static class ECDsaHelpers
    {
        public static ECDomainParameters GetDomain(ECDsaCurve curve)
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

            return new ECDomainParameters(ecCurve.Curve, ecCurve.G, ecCurve.N, ecCurve.H);
        }

        public static byte[] FromDER(byte[] signature)
        {
            using var decoder = new Asn1InputStream(signature);
            var seq = decoder.ReadObject() as DerSequence;
            if (seq == null || seq.Count != 2)
                throw new FormatException("Invalid DER Signature");
            var R = ((DerInteger)seq[0]).Value.ToByteArrayUnsigned();
            var S = ((DerInteger)seq[1]).Value.ToByteArrayUnsigned();

            byte[] concatenated = new byte[R.Length + S.Length];
            Buffer.BlockCopy(R, 0, concatenated, 0, R.Length);
            Buffer.BlockCopy(S, 0, concatenated, R.Length, S.Length);

            return concatenated;
        }

        public static byte[] ToDER(byte[] signature)
        {
            // We convert from concatenated "raw" R + S format to DER format that Bouncy Castle uses.
            return new DerSequence(
                // first 32 bytes is "R" number
                new DerInteger(new BigInteger(1, signature.Take(32).ToArray())),
                // last 32 bytes is "S" number
                new DerInteger(new BigInteger(1, signature.Skip(32).ToArray())))
                .GetDerEncoded();
        }
    }
}
