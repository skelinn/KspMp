using System.Text;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class ServerEditorTests
{
    private static byte[] Craft(string name, int parts) =>
        Encoding.UTF8.GetBytes($"ship = {name}\nPART\n{{\n\tpart = mk1pod_{parts}\n}}\n") is var raw
            ? DeflateCodec.Compress(raw, 0, raw.Length) : throw new InvalidOperationException();

    private static EditorSnapshotMsg Snapshot(string name, int parts, int revision) => new()
    {
        Facility = EditorFacilityKind.Vab, Revision = revision, ShipName = name, PartCount = parts,
        CraftDeflated = Craft(name, parts), ManifestDeflated = Array.Empty<byte>(),
    };

    private static ServerCore NewServer(LoopbackHub hub)
    {
        var server = new ServerCore(hub.CreateServer(), new ServerConfig(), new UniverseStore(null), _ => { });
        server.Start();
        return server;
    }

    private static TestClient Join(LoopbackHub hub, ServerCore server, string name, params TestClient[] others)
    {
        var client = new TestClient(hub, name);
        client.Start();
        TestClient.Pump(server, others.Append(client).ToArray());
        return client;
    }

    [Fact]
    public void SecondBuilderGetsTheCraftOnTheBenchAndEditsAreRelayed()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        a.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        TestClient.Pump(server, a, b);
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Equal(1, server.Editor.Get(EditorFacilityKind.Vab).Revision);
        Assert.Empty(b.Messages<EditorSnapshotMsg>()); // Bob is not in the VAB yet

        // Bob walks in and is handed the craft as it stands.
        b.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        TestClient.Pump(server, a, b);
        var handed = b.Last<EditorSnapshotMsg>();
        Assert.NotNull(handed);
        Assert.Equal("Rocket", handed!.Value.ShipName);
        Assert.Equal(3, handed.Value.PartCount);
        Assert.Equal(1, handed.Value.Revision);
        Assert.Equal(2, server.Editor.BuilderCount(EditorFacilityKind.Vab));

        // Bob adds a part; Alice sees it with the craft attached.
        b.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 4, 1), Channel.Bulk);
        TestClient.Pump(server, a, b);
        var relayed = a.Messages<EditorSnapshotMsg>().Last();
        Assert.Equal(4, relayed.PartCount);
        Assert.Equal(2, relayed.Revision);
        Assert.Equal(b.ClientId, relayed.FromClientId);
        Assert.NotEmpty(relayed.CraftDeflated);

        // Bob's own echo confirms the revision without resending the craft back to him.
        var echo = b.Messages<EditorSnapshotMsg>().Last();
        Assert.Equal(2, echo.Revision);
        Assert.Empty(echo.CraftDeflated);
    }

    [Fact]
    public void AStaleEditIsRefusedAndTheSenderIsResynced()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);
        a.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        b.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        TestClient.Pump(server, a, b);

        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 5, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Equal(1, server.Editor.Get(EditorFacilityKind.Vab).Revision);

        // Bob was still working from the empty bench: his edit is refused and he gets Alice's craft.
        b.Send(MessageId.EditorSnapshot, Snapshot("Bob's rocket", 99, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        var session = server.Editor.Get(EditorFacilityKind.Vab);
        Assert.Equal(1, session.Revision);
        Assert.Equal("Rocket", session.ShipName);
        var resync = b.Messages<EditorSnapshotMsg>().Last();
        Assert.Equal("Rocket", resync.ShipName);
        Assert.Equal(5, resync.PartCount);
        Assert.NotEmpty(resync.CraftDeflated);

        // On top of the current revision it is accepted.
        b.Send(MessageId.EditorSnapshot, Snapshot("Bob's rocket", 6, 1), Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Equal("Bob's rocket", server.Editor.Get(EditorFacilityKind.Vab).ShipName);
        Assert.Equal(2, server.Editor.Get(EditorFacilityKind.Vab).Revision);
    }

    [Fact]
    public void PresenceIsRelayedAndTheBenchClearsOnLaunchOrWhenEveryoneLeaves()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);
        a.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        b.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);

        a.Send(MessageId.EditorPresence, new EditorPresenceMsg { Facility = EditorFacilityKind.Vab, Holding = true, HeldPartName = "liquidEngine", CursorX = 1.5f }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, a, b);
        var seen = b.Messages<EditorPresenceMsg>().Last(p => p.ClientId == a.ClientId);
        Assert.True(seen.Holding);
        Assert.Equal("liquidEngine", seen.HeldPartName);
        Assert.Equal(1.5f, seen.CursorX);

        a.Send(MessageId.EditorLaunch, new EditorLaunchMsg { Facility = EditorFacilityKind.Vab, ShipName = "Rocket", LaunchSite = "LaunchPad" });
        TestClient.Pump(server, a, b);
        var launch = b.Last<EditorLaunchMsg>();
        Assert.NotNull(launch);
        Assert.Equal("Rocket", launch!.Value.ShipName);
        Assert.Equal(a.ClientId, launch.Value.FromClientId);
        Assert.False(server.Editor.Get(EditorFacilityKind.Vab).HasCraft);
        Assert.Equal(0, server.Editor.Get(EditorFacilityKind.Vab).Revision);

        // Everyone leaving also clears the bench, and a disconnect counts as leaving.
        b.Send(MessageId.EditorSnapshot, Snapshot("Second", 2, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.True(server.Editor.Get(EditorFacilityKind.Vab).HasCraft);
        a.Send(MessageId.EditorLeave, new EditorLeaveMsg());
        b.Stop();
        TestClient.Pump(server, a, b);
        Assert.Equal(0, server.Editor.BuilderCount(EditorFacilityKind.Vab));
        Assert.False(server.Editor.Get(EditorFacilityKind.Vab).HasCraft);
    }

    [Fact]
    public void TheVabAndSphAreSeparateWorkbenches()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);
        a.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        b.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Sph });
        TestClient.Pump(server, a, b);

        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Empty(b.Messages<EditorSnapshotMsg>());
        Assert.True(server.Editor.Get(EditorFacilityKind.Vab).HasCraft);
        Assert.False(server.Editor.Get(EditorFacilityKind.Sph).HasCraft);
        Assert.Equal(1, server.Editor.BuilderCount(EditorFacilityKind.Vab));
        Assert.Equal(1, server.Editor.BuilderCount(EditorFacilityKind.Sph));
    }
}
