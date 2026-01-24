using System.IO;
using PhantasmaPhoenix.Protocol.Carbon;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules
{
    /// <summary>
    /// Helpers for decoding Carbon NFT addresses (candidate for SDK).
    /// </summary>
    public static class TokenHelperExtensions
    {
        public static void UnpackNftAddress(Bytes32 address, out ulong tokenId, out ulong instanceId)
        {
            using var stream = new MemoryStream(address.bytes, writable: false);
            using var reader = new BinaryReader(stream);
            reader.BaseStream.Position = 16;
            reader.Read8(out tokenId);
            reader.Read8(out instanceId);
        }

        public static void UnpackNftAddress(string carbonNftAddress, out ulong tokenId, out ulong instanceId)
        {
            var address = new Bytes32(carbonNftAddress);
            UnpackNftAddress(address, out tokenId, out instanceId);
        }
    }
}
