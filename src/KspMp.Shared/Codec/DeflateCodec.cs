using System.IO;
using System.IO.Compression;

namespace KspMp.Shared.Codec
{
    /// <summary>Deflate helpers. Uses System.IO.Compression from System.dll, which KSP's Mono ships.</summary>
    public static class DeflateCodec
    {
        public static byte[] Compress(byte[] data, int offset, int count)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    deflate.Write(data, offset, count);
                }
                return output.ToArray();
            }
        }

        public static byte[] Decompress(byte[] data, int offset, int count)
        {
            using (var input = new MemoryStream(data, offset, count, writable: false))
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                inflate.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
