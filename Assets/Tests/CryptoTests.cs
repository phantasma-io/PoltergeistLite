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

        [UnityTest]
        public IEnumerator ECDsaSecP256k1_Deterministic()
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
                return Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignDeterministic(message, prikey, ECDsaCurve.Secp256k1);
            });

            var ecdsaSignature = (ECDsaSignature)signature;
            var signatureSerialized = signature.Serialize(); // signature.ToByteArray() gives same result

            Debug.Log("\nSignature (RAW concatenated r & s, hex):\n" + Base16.Encode(ecdsaSignature.Bytes));
            // Curve byte: ECDsaCurve enum: Secp256r1 = 0, Secp256k1 = 1.
            // Following is the format we use for signature:
            Debug.Log("\nSignature (curve byte + signature length + concatenated r & s, hex):\n" + Base16.Encode(signatureSerialized));

            var signatureDEREncoded = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.RSBytesToDER(ecdsaSignature.Bytes);

            Debug.Log("\nSignature (RAW DER-encoded, hex):\n" + Base16.Encode(signatureDEREncoded));
            Debug.Log("\nSignature (curve byte + signature length + DER-encoded, hex):\n" + Base16.Encode(signatureDEREncoded.Serialize()));

            // Since ECDsaSignature class not working for us,
            // we use signature .Bytes directly to verify it with Bouncy Castle.
            // Verifying concatenated signature / compressed Eth public key.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, ecdsaSignature.Bytes, ethPublicKeyCompressed, ECDsaCurve.Secp256k1));

            // Verifying DER signature.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signatureDEREncoded, ethPublicKeyCompressed, ECDsaCurve.Secp256k1, Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignatureFormat.DEREncoded));

            yield return null;
        }

        private void ECDsaTest(string pkHex, string message, string signatureReference = null)
        {
            Assert.IsTrue(pkHex.Length == 64);

            var privBytes = Base16.Decode(pkHex);
            var phantasmaKeys = new PhantasmaKeys(privBytes);

            var wif = phantasmaKeys.ToWIF();
            var ethKeys = EthereumKey.FromWIF(wif);
            Debug.Log("Eth address: " + ethKeys);

            var ethPublicKeyCompressed = ECDsa.GetPublicKey(privBytes, true, ECDsaCurve.Secp256k1);
            Debug.Log("Eth compressed public key: " + Base16.Encode(ethPublicKeyCompressed));

            var msgBytes = Encoding.ASCII.GetBytes(message);

            var hash = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Sha256Hash(msgBytes);
            Debug.Log("Message hash: " + Base16.Encode(hash));

            var signature = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignDeterministic(msgBytes, privBytes, ECDsaCurve.Secp256k1);
            var signatureHex = Base16.Encode(signature);

            if (signatureReference != null)
            {
                Assert.AreEqual(signatureHex, signatureReference);
            }

            // Curve byte: ECDsaCurve enum: Secp256r1 = 0, Secp256k1 = 1.
            // Following is the format we use for signature:
            Debug.Log("\nSignature (concatenated r & s, hex):\n" + signatureHex);

            var signatureDER = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.RSBytesToDER(signature);

            Debug.Log("\nSignature (RAW DER-encoded, hex):\n" + Base16.Encode(signatureDER));

            // Verifying concatenated signature / compressed Eth public key.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signature, ethPublicKeyCompressed, ECDsaCurve.Secp256k1));

            // Verifying DER signature.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signatureDER, ethPublicKeyCompressed, ECDsaCurve.Secp256k1, Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignatureFormat.DEREncoded));

            var signatureConvertedBack = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.TranscodeSignatureToConcat(signatureDER, 64);
            var signatureConvertedBackHex = Base16.Encode(signatureConvertedBack);
            Debug.Log("\nSignature (converted back from DER):\n" + signatureConvertedBackHex);
            Assert.AreEqual(signatureHex, signatureConvertedBackHex);

            // Verifying signature, converted back from DER.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signatureConvertedBack, ethPublicKeyCompressed, ECDsaCurve.Secp256k1));
        }

        [UnityTest]
        public IEnumerator ECDsaSecP256k1_DeterministicRaw()
        {
            // Eth address: 0x66571c32d77c4852be4c282eb952ba94efbeac20
            ECDsaTest("6f6784731c4e526c97fa6a97b6f22e96f307588c5868bc2c545248bc31207eb1", "Phantasma");
            ECDsaTest("6f6784731c4e526c97fa6a97b6f22e96f307588c5868bc2c545248bc31207eb1", "test message");
            // Eth address: 0xDf738B927DA923fe0A5Fd3aD2192990C68913e6a
            ECDsaTest("4ed773e5c8edc0487acef0011bc9ae8228287d4843f9d8477ff77c401ac59a49", "Phantasma");
            ECDsaTest("4ed773e5c8edc0487acef0011bc9ae8228287d4843f9d8477ff77c401ac59a49", "test message", "55DEB9E4D985834192AB8298C3DDA18EB7082C2A744EBDF7233D0A93FB00A4A9F4750F4A6F3FB3928C28690BE3A2BE52DEB95E1935E960FACBF7CC4AC4FDADCB");

            yield return null;
        }
    }

}
