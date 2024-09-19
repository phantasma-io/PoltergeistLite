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

        public static byte[] SignDeterministic(byte[] message, byte[] prikey, ECDsaCurve curve)
        {
            var messageHash = Phantasma.Core.Cryptography.Hashing.SHA256.ComputeHash(message);

            var privateKeyParameters = ECDsaHelpers.GetECPrivateKeyParameters(curve, prikey);

            var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));

            signer.Init(true, privateKeyParameters);

            var RS = signer.GenerateSignature(messageHash);
            var R = RS[0].ToByteArrayUnsigned();
            var S = RS[1].ToByteArrayUnsigned();

            return R.Concat(S).ToArray();
        }
    }
}
