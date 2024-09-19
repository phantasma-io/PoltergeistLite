using Org.BouncyCastle.Crypto.Digests;

namespace Phantasma.Core.Cryptography.Hashing
{
    public static class SHA256
    {
        public static byte[] ComputeHash(byte[] data)
        {
            return ComputeHash(data, 0, (uint)data.Length);
        }

        public static byte[] ComputeHash(byte[] data, uint offset, uint length)
        {
            var digest = new Sha256Digest();
            var output = new byte[digest.GetDigestSize()];
            digest.BlockUpdate(data, (int)offset, (int)length);
            digest.DoFinal(output, 0);
            return output;
        }
    }
}