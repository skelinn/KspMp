using KspMp.Server;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using Xunit;

namespace KspMp.Shared.Tests;

/// <summary>A scripted client on a loopback transport that records every decoded message it receives.</summary>
internal sealed class TestClient
{
    private readonly NetDataWriter _writer = new();

    public TestClient(LoopbackHub hub, string name, ushort protocolVersion = ProtocolVersion.Current)
    {
        Name = name;
        PlayerId = Guid.NewGuid();
        Transport = hub.CreateClient();
        Transport.PeerConnected += _ =>
        {
            ConnectedEvents++;
            Send(MessageId.Hello, new HelloMsg { ProtocolVersion = protocolVersion, ModVersion = "test", PlayerId = PlayerId, PlayerName = name, KspVersion = "1.12.5" }, Channel.Control);
        };
        Transport.PeerDisconnected += (_, reason) => { Disconnected = true; DisconnectReason = reason; };
        Transport.Received += OnReceived;
    }

    public string Name { get; }
    public Guid PlayerId { get; }
    public LoopbackTransport Transport { get; }
    public List<(MessageId Id, object Message)> Received { get; } = new();
    public int ConnectedEvents { get; private set; }
    public bool Disconnected { get; private set; }
    public string? DisconnectReason { get; private set; }
    public int ClientId { get; private set; }

    public void Start() => Transport.Start();
    public void Stop() => Transport.Stop();
    public void Poll() => Transport.Poll();

    public IEnumerable<T> Messages<T>() where T : struct => Received.Where(r => r.Message is T).Select(r => (T)r.Message);
    public T? Last<T>() where T : struct => Messages<T>().Cast<T?>().LastOrDefault();

    public void Send<T>(MessageId id, T message, Channel channel = Channel.Control, Delivery delivery = Delivery.ReliableOrdered) where T : INetSerializable
    {
        Envelope.Write(_writer, id, message);
        Transport.Send(PeerId.Server, _writer.Data, 0, _writer.Length, channel, delivery);
    }

    private void OnReceived(PeerId from, byte[] buffer, int offset, int length, Channel channel)
    {
        var reader = new NetDataReader(buffer, offset, length);
        Assert.True(Envelope.TryReadHeader(reader, out var id, out var flags, out _));
        var body = Envelope.OpenBody(reader, flags);
        object message = id switch
        {
            MessageId.Welcome => Envelope.Read<WelcomeMsg>(body),
            MessageId.Reject => Envelope.Read<RejectMsg>(body),
            MessageId.Pong => Envelope.Read<PongMsg>(body),
            MessageId.PlayerList => Envelope.Read<PlayerListMsg>(body),
            MessageId.PlayerJoined => Envelope.Read<PlayerJoinedMsg>(body),
            MessageId.PlayerLeft => Envelope.Read<PlayerLeftMsg>(body),
            MessageId.Chat => Envelope.Read<ChatMsg>(body),
            MessageId.TimeSync => Envelope.Read<TimeSyncMsg>(body),
            MessageId.VesselProto => Envelope.Read<VesselProtoMsg>(body),
            MessageId.VesselRemove => Envelope.Read<VesselRemoveMsg>(body),
            MessageId.VesselState => Envelope.Read<VesselStateMsg>(body),
            MessageId.AuthorityAssign => Envelope.Read<AuthorityAssignMsg>(body),
            MessageId.WarpState => Envelope.Read<WarpStateMsg>(body),
            _ => throw new Xunit.Sdk.XunitException("unexpected message " + id),
        };
        if (message is WelcomeMsg welcome) ClientId = welcome.ClientId;
        Received.Add((id, message));
    }

    /// <summary>Runs server and client polls a few times so queued loopback events settle.</summary>
    public static void Pump(ServerCore server, params TestClient[] clients)
    {
        for (var i = 0; i < 6; i++)
        {
            server.Poll();
            foreach (var c in clients) c.Poll();
        }
    }
}
