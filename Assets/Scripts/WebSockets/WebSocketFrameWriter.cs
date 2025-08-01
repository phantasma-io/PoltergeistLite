using System.IO;
using System;

namespace Poltergeist.WebSockets
{
    // see http://tools.ietf.org/html/rfc6455 for specification
    // see fragmentation section for sending multi part messages
    // EXAMPLE: For a text message sent as three fragments, 
    //   the first fragment would have an opcode of TextFrame and isLastFrame false,
    //   the second fragment would have an opcode of ContinuationFrame and isLastFrame false,
    //   the third fragment would have an opcode of ContinuationFrame and isLastFrame true.
    internal static class WebSocketFrameWriter
    {
        /// <summary>
        /// This is used for data masking so that web proxies don't cache the data
        /// Therefore, there are no cryptographic concerns
        /// </summary>
        private static readonly Random _random;
        
        static WebSocketFrameWriter()
        {
            _random = new Random((int)DateTime.Now.Ticks);
        }

        /// <summary>
        /// No async await stuff here because we are dealing with a memory stream
        /// </summary>
        /// <param name="opCode">The web socket opcode</param>
        /// <param name="fromPayload">Array segment to get payload data from</param>
        /// <param name="toStream">Stream to write to</param>
        /// <param name="isLastFrame">True is this is the last frame in this message (usually true)</param>
        public static void Write(WebSocketOpCode opCode, ArraySegment<byte> fromPayload, MemoryStream toStream, bool isLastFrame, bool isClient)
        {
            MemoryStream memoryStream = toStream;
            byte finBitSetAsByte = isLastFrame ? (byte)0x80 : (byte)0x00;
            byte byte1 = (byte)(finBitSetAsByte | (byte)opCode);
            memoryStream.WriteByte(byte1);

            // NB, set the mask flag if we are constructing a client frame
            byte maskBitSetAsByte = isClient ? (byte)0x80 : (byte)0x00;

            // depending on the size of the length we want to write it as a byte, ushort or ulong
            if (fromPayload.Count < 126)
            {
                byte byte2 = (byte)(maskBitSetAsByte | (byte)fromPayload.Count);
                memoryStream.WriteByte(byte2);
            }
            else if (fromPayload.Count <= ushort.MaxValue)
            {
                byte byte2 = (byte)(maskBitSetAsByte | 126);
                memoryStream.WriteByte(byte2);
                BinaryReaderWriter.WriteUShort((ushort)fromPayload.Count, memoryStream, false);
            }
            else
            {
                byte byte2 = (byte)(maskBitSetAsByte | 127);
                memoryStream.WriteByte(byte2);
                BinaryReaderWriter.WriteULong((ulong)fromPayload.Count, memoryStream, false);
            }

            // if we are creating a client frame then we MUST mack the payload as per the spec
            if (isClient)
            {
                byte[] maskKey = new byte[WebSocketFrameExtensions.MaskKeyLength];
                _random.NextBytes(maskKey);
                memoryStream.Write(maskKey, 0, maskKey.Length);

                // mask the payload
                ArraySegment<byte> maskKeyArraySegment = new ArraySegment<byte>(maskKey, 0, maskKey.Length);
                WebSocketFrameExtensions.ToggleMask(maskKeyArraySegment, fromPayload);
            }

            memoryStream.Write(fromPayload.Array, fromPayload.Offset, fromPayload.Count);
        }
    }
}
