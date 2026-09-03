using System.Text;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class ServerVesselTests
{
    private static readonly Guid VesselA = Guid.NewGuid();

    private static byte[] Proto(string name) =>
        Encoding.UTF8.GetBytes($"VESSEL\n{{\n\tpid = {VesselA}\n\tpersistentId = 42\n\tname = {name}\n\ttype = Ship\n\tPART\n\t{{\n\t\tname = mk1pod.v2\n\t}}\n}}\n")
            is var raw ? DeflateCodec.Compress(raw, 0, raw.Length) : throw new InvalidOperationException();

    private static VesselProtoMsg ProtoMsg(string name, ProtoReason reason = ProtoReason.FlightReady) => new()
    {
        VesselId = VesselA, PersistentId = 42, Name = name, VesselType = "Ship", Reason = reason, ProtoDeflated = Proto(name),
    };

    private static ServerCore NewServer(LoopbackHub hub, UniverseStore? universe = null)
    {
        var server = new ServerCore(hub.CreateServer(), new ServerConfig(), universe ?? new UniverseStore(null), _ => { });
        server.Start();
        return server;
    }

    private static TestClient Join(LoopbackHub hub, ServerCore server, string name, params TestClient[] others)
    {
        var client = new TestClient(hub, name);
        client.Start();
        TestClient.Pump(server, others.Append(client).ToArray());
        Assert.NotNull(client.Last<WelcomeMsg>());
        return client;
    }

    [Fact]
    public void SnapshotFromOwnerIsStoredRelayedAndSyncedToLateJoiners()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        a.Send(MessageId.VesselProto, ProtoMsg("Kerbal X"), Channel.Bulk);
        TestClient.Pump(server, a, b);

        var relayed = b.Last<VesselProtoMsg>();
        Assert.NotNull(relayed);
        Assert.Equal(VesselA, relayed!.Value.VesselId);
        Assert.Equal(a.ClientId, relayed.Value.OwnerClientId);
        Assert.Equal("Kerbal X", relayed.Value.Name);
        Assert.Equal(ProtoReason.FlightReady, relayed.Value.Reason);
        Assert.Null(a.Last<VesselProtoMsg>()); // never echoed to the sender
        Assert.Equal(a.ClientId, a.Last<AuthorityAssignMsg>()!.Value.OwnerClientId);
        Assert.Equal(1, server.Vessels.Count);

        var c = Join(hub, server, "Carol", a, b);
        var synced = c.Last<VesselProtoMsg>();
        Assert.NotNull(synced);
        Assert.Equal(ProtoReason.Sync, synced!.Value.Reason);
        Assert.Equal(a.ClientId, synced.Value.OwnerClientId);
        Assert.Equal(Proto("Kerbal X"), synced.Value.ProtoDeflated);
    }

    [Fact]
    public void StateIsRelayedOnlyFromTheOwner()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);
        a.Send(MessageId.VesselProto, ProtoMsg("Kerbal X"), Channel.Bulk);
        TestClient.Pump(server, a, b);

        b.Send(MessageId.VesselState, new VesselStateMsg { VesselId = VesselA, Ut = 5, Altitude = 999 }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, a, b);
        Assert.Null(a.Last<VesselStateMsg>());

        a.Send(MessageId.VesselState, new VesselStateMsg { VesselId = VesselA, Ut = 6, Altitude = 123.5, Landed = true }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, a, b);
        var state = b.Last<VesselStateMsg>();
        Assert.NotNull(state);
        Assert.Equal(123.5, state!.Value.Altitude);
        Assert.True(state.Value.Landed);
        Assert.True(server.Vessels.TryGet(VesselA, out var record) && record.HasState && record.LastState.Ut == 6);
    }

    [Fact]
    public void AuthorityIsDeniedWhileOwnedAndReleasedWhenTheOwnerLeaves()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);
        a.Send(MessageId.VesselProto, ProtoMsg("Kerbal X"), Channel.Bulk);
        TestClient.Pump(server, a, b);

        b.Send(MessageId.AuthorityRequest, new AuthorityRequestMsg { VesselId = VesselA });
        TestClient.Pump(server, a, b);
        var denied = b.Last<AuthorityAssignMsg>()!.Value;
        Assert.Equal(AuthorityReason.Denied, denied.Reason);
        Assert.Equal(a.ClientId, denied.OwnerClientId);

        a.Stop();
        TestClient.Pump(server, a, b);
        var released = b.Messages<AuthorityAssignMsg>().Last();
        Assert.Equal(AuthorityReason.OwnerLeft, released.Reason);
        Assert.Equal(0, released.OwnerClientId);
        Assert.True(server.Authority.IsUnowned(VesselA));

        b.Send(MessageId.AuthorityRequest, new AuthorityRequestMsg { VesselId = VesselA });
        TestClient.Pump(server, b);
        var granted = b.Messages<AuthorityAssignMsg>().Last();
        Assert.Equal(AuthorityReason.Granted, granted.Reason);
        Assert.Equal(b.ClientId, granted.OwnerClientId);

        b.Send(MessageId.AuthorityRelease, new AuthorityReleaseMsg { VesselId = VesselA });
        TestClient.Pump(server, b);
        Assert.Equal(AuthorityReason.Released, b.Messages<AuthorityAssignMsg>().Last().Reason);
        Assert.True(server.Authority.IsUnowned(VesselA));
    }

    [Fact]
    public void RemoveDeletesTheVesselAndTellsEveryoneElse()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);
        a.Send(MessageId.VesselProto, ProtoMsg("Kerbal X"), Channel.Bulk);
        TestClient.Pump(server, a, b);

        b.Send(MessageId.VesselRemove, new VesselRemoveMsg { VesselId = VesselA, Reason = "hijack" }, Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Equal(1, server.Vessels.Count);

        a.Send(MessageId.VesselRemove, new VesselRemoveMsg { VesselId = VesselA, Reason = "recovered" }, Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Equal(0, server.Vessels.Count);
        Assert.Equal("recovered", b.Last<VesselRemoveMsg>()!.Value.Reason);
        Assert.True(server.Authority.IsUnowned(VesselA));
    }

    [Fact]
    public void VesselsPersistAsReadableFilesAndReloadOnRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kspmp-universe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hub1 = new LoopbackHub();
            var server1 = new ServerCore(hub1.CreateServer(), new ServerConfig(), new UniverseStore(dir), _ => { });
            server1.Start();
            var a = Join(hub1, server1, "Alice");
            a.Send(MessageId.VesselProto, ProtoMsg("Kerbal X"), Channel.Bulk);
            TestClient.Pump(server1, a);
            server1.Stop();

            var file = Path.Combine(dir, "vessels", VesselA + ".cfg");
            Assert.True(File.Exists(file));
            Assert.Contains("name = Kerbal X", File.ReadAllText(file));

            var hub2 = new LoopbackHub();
            using var server2 = new ServerCore(hub2.CreateServer(), new ServerConfig(), new UniverseStore(dir), _ => { });
            server2.Start();
            Assert.Equal(1, server2.Vessels.Count);
            Assert.True(server2.Vessels.TryGet(VesselA, out var record));
            Assert.Equal("Kerbal X", record!.Name);
            Assert.Equal(42u, record.PersistentId);

            var b = Join(hub2, server2, "Bob");
            var synced = b.Last<VesselProtoMsg>();
            Assert.NotNull(synced);
            Assert.Equal(0, synced!.Value.OwnerClientId);
            var text = Encoding.UTF8.GetString(DeflateCodec.Decompress(synced.Value.ProtoDeflated, 0, synced.Value.ProtoDeflated.Length));
            Assert.Contains("mk1pod.v2", text);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
