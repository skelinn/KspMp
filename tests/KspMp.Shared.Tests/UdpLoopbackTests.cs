using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using Xunit;

namespace KspMp.Shared.Tests;

public class UdpLoopbackTests
{
    [Fact]
    public async Task ClientHandshakesAndPingsOverUdpLoopback()
    {
        var serverTransport = new LiteNetLibTransport(new TransportOptions { IsServer = true, Port = 0 });
        using var server = new ServerCore(serverTransport, new ServerConfig(), new UniverseStore(null), _ => { });
        server.Start();

        var client = new LiteNetLibTransport(new TransportOptions { IsServer = false, Address = "127.0.0.1", Port = serverTransport.LocalPort });
        var writer = new NetDataWriter();
        int? clientId = null;
        var ponged = false;

        client.PeerConnected += _ =>
        {
            Envelope.Write(writer, MessageId.Hello, new HelloMsg { ProtocolVersion = ProtocolVersion.Current, PlayerName = "Val", PlayerId = Guid.NewGuid() });
            client.Send(PeerId.Server, writer.Data, 0, writer.Length, Channel.Control, Delivery.ReliableOrdered);
        };
        client.Received += (_, buffer, offset, length, _) =>
        {
            var reader = new NetDataReader(buffer, offset, length);
            Assert.True(Envelope.TryReadHeader(reader, out var id, out var flags, out _));
            var body = Envelope.OpenBody(reader, flags);
            switch (id)
            {
                case MessageId.Welcome:
                    clientId = Envelope.Read<WelcomeMsg>(body).ClientId;
                    Envelope.Write(writer, MessageId.Ping, new PingMsg { ClientTicks = 123 });
                    client.Send(PeerId.Server, writer.Data, 0, writer.Length, Channel.State, Delivery.ReliableOrdered);
                    break;
                case MessageId.Pong:
                    Assert.Equal(123, Envelope.Read<PongMsg>(body).ClientTicks);
                    ponged = true;
                    break;
            }
        };
        client.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ponged && DateTime.UtcNow < deadline)
        {
            server.Poll();
            client.Poll();
            await Task.Delay(5);
        }

        client.Stop();
        Assert.Equal(1, clientId);
        Assert.True(ponged, "no pong within 5 s");
        Assert.Equal(1, server.OnlineCount);
    }

    [Fact]
    public async Task ProtocolMismatchIsRejected()
    {
        var serverTransport = new LiteNetLibTransport(new TransportOptions { IsServer = true, Port = 0 });
        using var server = new ServerCore(serverTransport, new ServerConfig(), new UniverseStore(null), _ => { });
        server.Start();

        var client = new LiteNetLibTransport(new TransportOptions { Address = "127.0.0.1", Port = serverTransport.LocalPort });
        var writer = new NetDataWriter();
        string? rejection = null;
        var disconnected = false;
        client.PeerConnected += _ =>
        {
            Envelope.Write(writer, MessageId.Hello, new HelloMsg { ProtocolVersion = 999, PlayerName = "Bob" });
            client.Send(PeerId.Server, writer.Data, 0, writer.Length, Channel.Control, Delivery.ReliableOrdered);
        };
        client.Received += (_, buffer, offset, length, _) =>
        {
            var reader = new NetDataReader(buffer, offset, length);
            if (Envelope.TryReadHeader(reader, out var id, out var flags, out _) && id == MessageId.Reject)
                rejection = Envelope.Read<RejectMsg>(Envelope.OpenBody(reader, flags)).Reason;
        };
        client.PeerDisconnected += (_, _) => disconnected = true;
        client.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(rejection != null && disconnected) && DateTime.UtcNow < deadline)
        {
            server.Poll();
            client.Poll();
            await Task.Delay(5);
        }
        client.Stop();

        Assert.Contains("Protocol version mismatch", rejection);
        Assert.True(disconnected);
    }
}
