using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using Xunit;

namespace KspMp.Shared.Tests;

public class EnvelopeTests
{
    private static readonly HelloMsg Sample = new()
    {
        ProtocolVersion = 7,
        ModVersion = "0.1.0",
        PlayerId = Guid.NewGuid(),
        PlayerName = "Jeb",
        KspVersion = "1.12.5.3190",
    };

    [Theory]
    [InlineData(EnvelopeFlags.None)]
    [InlineData(EnvelopeFlags.Deflated)]
    [InlineData(EnvelopeFlags.HasSeq)]
    [InlineData(EnvelopeFlags.Deflated | EnvelopeFlags.HasSeq)]
    public void RoundTripsWithFlags(EnvelopeFlags flags)
    {
        var writer = new NetDataWriter();
        Envelope.Write(writer, MessageId.Hello, Sample, flags, seq: 42);

        var reader = new NetDataReader(writer.Data, 0, writer.Length);
        Assert.True(Envelope.TryReadHeader(reader, out var id, out var readFlags, out var seq));
        Assert.Equal(MessageId.Hello, id);
        Assert.Equal(flags, readFlags);
        Assert.Equal((flags & EnvelopeFlags.HasSeq) != 0 ? 42u : 0u, seq);

        var hello = Envelope.Read<HelloMsg>(Envelope.OpenBody(reader, readFlags));
        Assert.Equal(Sample.ProtocolVersion, hello.ProtocolVersion);
        Assert.Equal(Sample.ModVersion, hello.ModVersion);
        Assert.Equal(Sample.PlayerId, hello.PlayerId);
        Assert.Equal(Sample.PlayerName, hello.PlayerName);
        Assert.Equal(Sample.KspVersion, hello.KspVersion);
    }

    [Fact]
    public void RejectsTruncatedHeader()
    {
        var reader = new NetDataReader(new byte[] { 1, 0 });
        Assert.False(Envelope.TryReadHeader(reader, out _, out _, out _));
    }
}
