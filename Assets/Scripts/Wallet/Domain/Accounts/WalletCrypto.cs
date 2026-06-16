using System;
using PhantasmaPhoenix.Cryptography;

namespace Poltergeist
{
    // Password hashing and WIF encryption for wallet accounts. Pure and stateless, so it lives
    // outside AccountManager and can be reasoned about (and tested) on its own.
    public static class WalletCrypto
    {
        public static readonly int PasswordIterations = 100000;
        private static readonly int PasswordSaltByteSize = 64;
        private static readonly int PasswordHashByteSize = 32;

        public static void GetPasswordHash(string password, int passwordIterations, out string salt, out string passwordHash)
        {
            BouncyCastleHashing hashing = new BouncyCastleHashing();
            salt = Convert.ToBase64String(hashing.CreateSalt(PasswordSaltByteSize));
            passwordHash = hashing.PBKDF2_SHA256_GetHash(password, salt, passwordIterations, PasswordHashByteSize);
        }

        public static void GetPasswordHashBySalt(string password, int passwordIterations, string salt, out string passwordHash)
        {
            BouncyCastleHashing hashing = new BouncyCastleHashing();
            passwordHash = hashing.PBKDF2_SHA256_GetHash(password, salt, passwordIterations, PasswordHashByteSize);
        }

        public static string EncryptString(string stringToEncrypt, string key, out string iv)
        {
            var ivBytes = new byte[16];

            //Set up
            var keyParam = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(Convert.FromBase64String(key));

            var secRandom = new Org.BouncyCastle.Security.SecureRandom();
            secRandom.NextBytes(ivBytes);

            var keyParamWithIV = new Org.BouncyCastle.Crypto.Parameters.ParametersWithIV(keyParam, ivBytes, 0, 16);

            var engine = new Org.BouncyCastle.Crypto.Engines.AesEngine();
            var blockCipher = new Org.BouncyCastle.Crypto.Modes.CbcBlockCipher(engine); //CBC
            var cipher = new Org.BouncyCastle.Crypto.Paddings.PaddedBufferedBlockCipher(blockCipher); //Default scheme is PKCS5/PKCS7

            // Encrypt
            cipher.Init(true, keyParamWithIV);
            var inputBytes = System.Text.Encoding.UTF8.GetBytes(stringToEncrypt);
            var outputBytes = new byte[cipher.GetOutputSize(inputBytes.Length)];
            var length = cipher.ProcessBytes(inputBytes, outputBytes, 0);
            cipher.DoFinal(outputBytes, length); //Do the final block

            iv = Convert.ToBase64String(ivBytes);
            return Convert.ToBase64String(outputBytes);
        }

        public static string DecryptString(string stringToDecrypt, string key, string iv)
        {
            //Set up
            var keyParam = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(Convert.FromBase64String(key));
            var ivBytes = Convert.FromBase64String(iv);
            var keyParamWithIV = new Org.BouncyCastle.Crypto.Parameters.ParametersWithIV(keyParam, ivBytes, 0, 16);

            var engine = new Org.BouncyCastle.Crypto.Engines.AesEngine();
            var blockCipher = new Org.BouncyCastle.Crypto.Modes.CbcBlockCipher(engine); //CBC
            var cipher = new Org.BouncyCastle.Crypto.Paddings.PaddedBufferedBlockCipher(blockCipher);

            cipher.Init(false, keyParamWithIV);
            var inputBytes = Convert.FromBase64String(stringToDecrypt);
            var resultExtraSize = new byte[cipher.GetOutputSize(inputBytes.Length)];
            var length = cipher.ProcessBytes(inputBytes, resultExtraSize, 0);
            length += cipher.DoFinal(resultExtraSize, length); //Do the final block

            var result = new byte[length];
            Array.Copy(resultExtraSize, result, length);

            return System.Text.Encoding.UTF8.GetString(result);
        }
    }
}
