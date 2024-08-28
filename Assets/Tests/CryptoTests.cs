using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;
using Poltergeist.PhantasmaLegacy.Ethereum;
using Phantasma.Core.Numerics;
using Phantasma.Core.Cryptography;
using Phantasma.Core.Cryptography.ECDsa;
using Phantasma.Core.Domain;

namespace Phantasma.Tests
{
    public class CryptoTests
    {
        [UnityTest]
        public IEnumerator ECDsaSecP256k1()
        {
            // Eth address: "0x66571c32d77c4852be4c282eb952ba94efbeac20";
            var key = "6f6784731c4e526c97fa6a97b6f22e96f307588c5868bc2c545248bc31207eb1";
            Assert.IsTrue(key.Length == 64);

            var privBytes = Base16.Decode(key);
            var phantasmaKeys = new PhantasmaKeys(privBytes);

            var wif = phantasmaKeys.ToWIF();
            var ethKeys = EthereumKey.FromWIF(wif);
            Debug.Log("Eth address: " + ethKeys);

            var ethPublicKeyCompressed = ECDsa.GetPublicKey(privBytes, true, ECDsaCurve.Secp256k1);
            Debug.Log("Eth compressed public key: " + Base16.Encode(ethPublicKeyCompressed));
            var ethPublicKeyUncompressed = ECDsa.GetPublicKey(privBytes, false, ECDsaCurve.Secp256k1).Skip(1).ToArray();
            Debug.Log("Eth uncompressed public key: " + Base16.Encode(ethPublicKeyUncompressed));

            var msgBytes = Encoding.ASCII.GetBytes("Phantasma");
            var signature = ethKeys.Sign(msgBytes, (message, prikey, pubkey) =>
            {
                return Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Sign(message, prikey, ECDsaCurve.Secp256k1);
            });

            var ecdsaSignature = (ECDsaSignature)signature;
            var signatureSerialized = signature.Serialize(); // signature.ToByteArray() gives same result

            Debug.Log("\nSignature (RAW concatenated r & s, hex):\n" + Base16.Encode(ecdsaSignature.Bytes));
            // Curve byte: ECDsaCurve enum: Secp256r1 = 0, Secp256k1 = 1.
            // Following is the format we use for signature:
            Debug.Log("\nSignature (curve byte + signature length + concatenated r & s, hex):\n" + Base16.Encode(signatureSerialized));

            var signatureDEREncoded = ethKeys.Sign(msgBytes, (message, prikey, pubkey) =>
            {
                return Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Sign(message, prikey, ECDsaCurve.Secp256k1, Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignatureFormat.DEREncoded);
            });

            var ecdsaSignatureDEREncoded = (ECDsaSignature)signatureDEREncoded;

            Debug.Log("\nSignature (RAW DER-encoded, hex):\n" + Base16.Encode(ecdsaSignatureDEREncoded.Bytes));
            Debug.Log("\nSignature (curve byte + signature length + DER-encoded, hex):\n" + Base16.Encode(signatureDEREncoded.Serialize()));

            // Since ECDsaSignature class not working for us,
            // we use signature .Bytes directly to verify it with Bouncy Castle.
            // Verifying concatenated signature / compressed Eth public key.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, ecdsaSignature.Bytes, ethPublicKeyCompressed, ECDsaCurve.Secp256k1));

            // Verifying concatenated signature / uncompressed Eth public key.
            // Not working with Bouncy Castle.
            // Assert.IsTrue(Phantasma.Neo.Utils.CryptoUtils.Verify(msgBytes, ecdsaSignature.Bytes, ethPublicKeyUncompressed, ECDsaCurve.Secp256k1));

            // Verifying DER signature.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, ecdsaSignatureDEREncoded.Bytes, ethPublicKeyCompressed, ECDsaCurve.Secp256k1, Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignatureFormat.DEREncoded));

            // This method we cannot use, it gives "System.NotImplementedException : The method or operation is not implemented."
            // exception in Unity, because Unity does not fully support .NET cryptography.
            // Assert.IsTrue(((ECDsaSignature)signature).Verify(msgBytes, Address.FromKey(ethKeys)));

            // Failes for same reason: "System.NotImplementedException".
            // Assert.IsTrue(CryptoExtensions.VerifySignatureECDsa(msgBytes, signatureSerialized, ethPublicKeyCompressed, ECDsaCurve.Secp256k1));

            yield return null;
        }
    }

}
