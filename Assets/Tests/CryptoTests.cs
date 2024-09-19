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
        public IEnumerator WifPkTest()
        {
            var wif = "Kyry8sMHFzx5DfubcqyGMQByaHQBtdyALBjAqcx9Lx1YtSZjy2vZ";
            var pkHex = "4ed773e5c8edc0487acef0011bc9ae8228287d4843f9d8477ff77c401ac59a49";

            var keyFromWif = EthereumKey.FromWIF(wif);

            Assert.AreEqual(keyFromWif.PublicKey, keyFromWif.CompressedPublicKey);

            var pkBytesFromHex = Base16.Decode(pkHex);
            Assert.AreEqual(pkBytesFromHex.Length, 32);

            var keyFromHex = new EthereumKey(pkBytesFromHex);

            var pkBytesFromWif = EthereumKey.FromWIFToBytes(wif);
            Assert.AreEqual(pkBytesFromWif, pkBytesFromHex);
            Assert.AreEqual(pkBytesFromWif, keyFromWif.PrivateKey);
            Assert.AreEqual(pkBytesFromWif, keyFromHex.PrivateKey);
            Assert.AreEqual(pkBytesFromWif.Length, 32);

            Assert.AreEqual(keyFromWif.PrivateKey.Length, 32);
            Assert.AreEqual(keyFromHex.PrivateKey.Length, 32);
            Assert.AreEqual(keyFromWif.PublicKey.Length, 33);
            Assert.AreEqual(keyFromHex.PublicKey.Length, 33);

            Assert.AreEqual(keyFromWif.PublicKey, keyFromHex.PublicKey);
            Assert.AreEqual(keyFromWif.PrivateKey, keyFromHex.PrivateKey);

            yield return null;
        }

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

            var signatureDEREncoded = ECDsaHelpers.ToDER(ecdsaSignature.Bytes);

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

        private void ECDsaTest(ECDsaCurve curve, string pkHex, string message, string signatureReference = null)
        {
            Debug.Log("\n\n\nCurve: " + curve);
            Assert.IsTrue(pkHex.Length == 64);

            var privBytes = Base16.Decode(pkHex);
            var phantasmaKeys = new PhantasmaKeys(privBytes);

            var wif = phantasmaKeys.ToWIF();
            var ethKeys = EthereumKey.FromWIF(wif);
            Debug.Log("Eth address: " + ethKeys);

            var ethPublicKeyCompressed = ECDsa.GetPublicKey(privBytes, true, curve);
            var ethPublicKeyUncompressed = ECDsa.GetPublicKey(privBytes, false, curve).Skip(1).ToArray();
            Debug.Log("Eth compressed public key: " + Base16.Encode(ethPublicKeyCompressed));

            var msgBytes = Encoding.ASCII.GetBytes(message);

            var hash = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Sha256Hash(msgBytes);
            Debug.Log("Message hash: " + Base16.Encode(hash));

            var signature = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignDeterministic(msgBytes, privBytes, curve);
            var signatureHex = Base16.Encode(signature);

            var signature2 = Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Sign(msgBytes, privBytes, curve);
            Assert.IsTrue(ECDsa.Verify(msgBytes, signature2, ethPublicKeyCompressed, curve));
            Assert.IsTrue(ECDsa.Verify(msgBytes, signature2, ethPublicKeyUncompressed, curve));
            var signature3 = ECDsa.Sign(msgBytes, privBytes, curve);
            Assert.IsTrue(ECDsa.Verify(msgBytes, signature3, ethPublicKeyCompressed, curve));
            Assert.IsTrue(ECDsa.Verify(msgBytes, signature3, ethPublicKeyUncompressed, curve));

            if (signatureReference != null)
            {
                Assert.AreEqual(signatureHex, signatureReference);
            }

            // Curve byte: ECDsaCurve enum: Secp256r1 = 0, Secp256k1 = 1.
            // Following is the format we use for signature:
            Debug.Log("\nSignature (concatenated r & s, hex):\n" + signatureHex);

            var signatureDER = ECDsaHelpers.ToDER(signature);

            Debug.Log("\nSignature (RAW DER-encoded, hex):\n" + Base16.Encode(signatureDER));

            // Verifying concatenated signature / compressed Eth public key.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signature, ethPublicKeyCompressed, curve));

            // Verifying concatenated signature / uncompressed Eth public key.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signature, ethPublicKeyUncompressed, curve));

            // Verifying concatenated signature / compressed Eth public key.
            Assert.IsTrue(ECDsa.Verify(msgBytes, signature, ethPublicKeyCompressed, curve));

            // Verifying concatenated signature / uncompressed Eth public key.
            Assert.IsTrue(ECDsa.Verify(msgBytes, signature, ethPublicKeyUncompressed, curve));

            // Verifying DER signature.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signatureDER, ethPublicKeyCompressed, curve, Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.SignatureFormat.DEREncoded));

            // Verifying DER signature (unsupported).
            Assert.IsFalse(ECDsa.Verify(msgBytes, signatureDER, ethPublicKeyCompressed, curve));

            var signatureConvertedBack = ECDsaHelpers.FromDER(signatureDER);
            var signatureConvertedBackHex = Base16.Encode(signatureConvertedBack);
            Debug.Log("\nSignature (converted back from DER):\n" + signatureConvertedBackHex);
            Assert.AreEqual(signatureHex, signatureConvertedBackHex);

            // Verifying signature, converted back from DER.
            Assert.IsTrue(Poltergeist.PhantasmaLegacy.Cryptography.CryptoUtils.Verify(msgBytes, signatureConvertedBack, ethPublicKeyCompressed, curve));
        }

        [UnityTest]
        public IEnumerator ECDsaSecP256k1_Mixed()
        {
            // Eth address: 0x66571c32d77c4852be4c282eb952ba94efbeac20
            ECDsaTest(ECDsaCurve.Secp256k1, "6f6784731c4e526c97fa6a97b6f22e96f307588c5868bc2c545248bc31207eb1", "Phantasma");
            ECDsaTest(ECDsaCurve.Secp256r1, "6f6784731c4e526c97fa6a97b6f22e96f307588c5868bc2c545248bc31207eb1", "Phantasma");
            ECDsaTest(ECDsaCurve.Secp256k1, "6f6784731c4e526c97fa6a97b6f22e96f307588c5868bc2c545248bc31207eb1", "test message");
            ECDsaTest(ECDsaCurve.Secp256r1, "6f6784731c4e526c97fa6a97b6f22e96f307588c5868bc2c545248bc31207eb1", "test message");
            // Eth address: 0xDf738B927DA923fe0A5Fd3aD2192990C68913e6a
            ECDsaTest(ECDsaCurve.Secp256k1, "4ed773e5c8edc0487acef0011bc9ae8228287d4843f9d8477ff77c401ac59a49", "Phantasma");
            ECDsaTest(ECDsaCurve.Secp256r1, "4ed773e5c8edc0487acef0011bc9ae8228287d4843f9d8477ff77c401ac59a49", "Phantasma");
            ECDsaTest(ECDsaCurve.Secp256k1, "4ed773e5c8edc0487acef0011bc9ae8228287d4843f9d8477ff77c401ac59a49", "test message", "55DEB9E4D985834192AB8298C3DDA18EB7082C2A744EBDF7233D0A93FB00A4A9F4750F4A6F3FB3928C28690BE3A2BE52DEB95E1935E960FACBF7CC4AC4FDADCB");


            ECDsaTest(ECDsaCurve.Secp256k1, "4ed773e5c8edc0487acef0011bc9ae8228287d4843f9d8477ff77c401ac59a49", "I have signed this message with my Phantasma, Ethereum and Neo Legacy signatures to prove that following addresses belong to me and were derived from private key that belongs to me and to confirm my willingness to swap funds across these addresses upon my request. My public addresses are:\n" +
"Phantasma address: P2KHhbVZWDv1ZLLoJccN3PUAb9x9BqRnUyH3ZEhu5YwBeJQ\n" +
"Ethereum address: 0xDf738B927DA923fe0A5Fd3aD2192990C68913e6a\n" +
"Ethereum public key: 5D3F7F469803C68C12B8F731576C74A9B5308484FD3B425D87C35CAED0A2E398C7AC626D916A1D65E23F673A55E6B16FFC1ABD673F3EF6AE8D5E6A0F99784A56\n" +
"Neo Legacy address: Ae3aEA6CpvckvypAUShj2CLsy7sfynKUzj\n" +
"Neo Legacy public key: 183A301779007BF42DD7B5247587585B0524E13989F964C2A8E289A0CDC91F001765FCC3B4CEE5ED274C4A8B6D80978BDFED678210458CE264D4A4DAB3923EE6",
"E3E1FCD85385675F9E3508630570C545DECCD1241C7A8FFF523D2AC500D6F68745E43975DBF871C99504100B8DD6715F036FA51EFF9EB8B79D1E31FD555E78FC");

            yield return null;
        }
    }

}
