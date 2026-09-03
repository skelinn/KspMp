using System.Text;
using KspMp.Shared.Codec;
using Xunit;

namespace KspMp.Shared.Tests;

public class DeflateCodecTests
{
    [Fact]
    public void RoundTripsText()
    {
        var raw = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("PART { name = mk1pod }\n", 500)));
        var packed = DeflateCodec.Compress(raw, 0, raw.Length);
        Assert.True(packed.Length < raw.Length / 4, $"expected real compression, got {raw.Length} -> {packed.Length}");
        Assert.Equal(raw, DeflateCodec.Decompress(packed, 0, packed.Length));
    }

    [Fact]
    public void RoundTripsEmptyAndOffsetSlices()
    {
        Assert.Empty(DeflateCodec.Decompress(DeflateCodec.Compress(Array.Empty<byte>(), 0, 0), 0, DeflateCodec.Compress(Array.Empty<byte>(), 0, 0).Length));
        var buffer = new byte[] { 9, 9, 1, 2, 3, 4, 5, 9, 9 };
        var packed = DeflateCodec.Compress(buffer, 2, 5);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, DeflateCodec.Decompress(packed, 0, packed.Length));
    }
}
