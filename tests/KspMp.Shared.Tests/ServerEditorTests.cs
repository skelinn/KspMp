using System.Text;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

/// <summary>
/// Per-player workbenches. Everyone building gets their own bench and has to ask to join somebody else's, which
/// is the opposite of the old behaviour: two players who both opened the VAB used to land on one craft and fight
/// over it, each losing edits to the other's revision.
/// </summary>
public class ServerEditorTests
{
    private static byte[] Craft(string name, int parts) =>
        Encoding.UTF8.GetBytes($"ship = {name}\nPART\n{{\n\tpart = mk1pod_{parts}\n}}\n") is var raw
            ? DeflateCodec.Compress(raw, 0, raw.Length) : throw new InvalidOperationException();

    private static EditorSnapshotMsg Snapshot(string name, int parts, int revision, int session = 0) => new()
    {
        Facility = EditorFacilityKind.Vab, Revision = revision, ShipName = name, PartCount = parts,
        CraftDeflated = Craft(name, parts), ManifestDeflated = Array.Empty<byte>(), SessionOwnerClientId = session,
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

    private static void OpenVab(ServerCore server, TestClient client, params TestClient[] all)
    {
        client.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Vab });
        TestClient.Pump(server, all);
    }

    private static EditorSessionInfo? SessionOf(TestClient client, int ownerClientId)
    {
        var list = client.Last<EditorSessionListMsg>();
        if (list == null || list.Value.Sessions == null) return null;
        foreach (var session in list.Value.Sessions)
            if (session.OwnerClientId == ownerClientId) return session;
        return null;
    }

    [Fact]
    public void EveryoneStartsOnTheirOwnBenchAndTheListSaysWhoIsBuildingWhere()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        OpenVab(server, a, a, b);
        OpenVab(server, b, a, b);

        Assert.Equal(2, server.Editor.Sessions.Count());
        Assert.NotNull(SessionOf(b, a.ClientId));
        Assert.NotNull(SessionOf(b, b.ClientId));

        // Alice builds. Bob is in his own VAB and must not see a single part of it.
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Equal(1, server.Editor.Get(a.ClientId)!.Revision);
        Assert.Empty(b.Messages<EditorSnapshotMsg>());
        Assert.Equal("Rocket", SessionOf(b, a.ClientId)!.Value.ShipName);
        Assert.Equal(3, SessionOf(b, a.ClientId)!.Value.PartCount);
        Assert.Equal(0, SessionOf(b, b.ClientId)!.Value.PartCount);
    }

    [Fact]
    public void JoiningABenchHandsOverTheCraftAndRelaysEditsBothWays()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        OpenVab(server, a, a, b);
        OpenVab(server, b, a, b);
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);

        b.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = a.ClientId });
        TestClient.Pump(server, a, b);
        var handed = b.Last<EditorSnapshotMsg>();
        Assert.NotNull(handed);
        Assert.Equal("Rocket", handed!.Value.ShipName);
        Assert.Equal(3, handed.Value.PartCount);
        Assert.Equal(1, handed.Value.Revision);
        Assert.Equal(a.ClientId, handed.Value.SessionOwnerClientId);
        Assert.Equal(2, server.Editor.BuilderCount(a.ClientId));
        Assert.Null(server.Editor.Get(b.ClientId));   // a guest keeps no bench of their own

        // Bob edits at the revision he was given: accepted, and Alice sees it.
        b.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 4, 1, a.ClientId), Channel.Bulk);
        TestClient.Pump(server, a, b);
        var relayed = a.Messages<EditorSnapshotMsg>().Last();
        Assert.Equal(4, relayed.PartCount);
        Assert.Equal(b.ClientId, relayed.FromClientId);
        Assert.Equal(2, server.Editor.Get(a.ClientId)!.Revision);

        // A stale edit is refused, and the sender is handed the current craft rather than overwriting it.
        b.Send(MessageId.EditorSnapshot, Snapshot("Stale", 9, 1, a.ClientId), Channel.Bulk);
        TestClient.Pump(server, a, b);
        Assert.Equal(2, server.Editor.Get(a.ClientId)!.Revision);
        Assert.Equal("Rocket", server.Editor.Get(a.ClientId)!.ShipName);
        var corrected = b.Messages<EditorSnapshotMsg>().Last();
        Assert.Equal("Rocket", corrected.ShipName);
        Assert.Equal(4, corrected.PartCount);

        // Cursors are relayed within the session.
        b.Send(MessageId.EditorPresence, new EditorPresenceMsg { Facility = EditorFacilityKind.Vab, Holding = true, HeldPartName = "liquidEngine", CursorX = 1.5f, SessionOwnerClientId = a.ClientId }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, a, b);
        var seen = a.Last<EditorPresenceMsg>();
        Assert.NotNull(seen);
        Assert.Equal(b.ClientId, seen!.Value.ClientId);
        Assert.True(seen.Value.Holding);
        Assert.Equal("liquidEngine", seen.Value.HeldPartName);
    }

    [Fact]
    public void LeavingReturnsToAnOwnEmptyBench()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        OpenVab(server, a, a, b);
        OpenVab(server, b, a, b);
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        b.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = a.ClientId });
        TestClient.Pump(server, a, b);

        b.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = 0 });
        TestClient.Pump(server, a, b);
        Assert.Equal(1, server.Editor.BuilderCount(a.ClientId));
        Assert.NotNull(server.Editor.Get(b.ClientId));
        Assert.False(server.Editor.Get(b.ClientId)!.HasCraft);
        // Alice's craft is untouched by Bob coming and going.
        Assert.Equal("Rocket", server.Editor.Get(a.ClientId)!.ShipName);
    }

    [Fact]
    public void TheOwnerLeavingTheEditorDissolvesTheSession()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        OpenVab(server, a, a, b);
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        b.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = a.ClientId });
        TestClient.Pump(server, a, b);

        a.Send(MessageId.EditorLeave, new EditorLeaveMsg());
        TestClient.Pump(server, a, b);
        Assert.Null(server.Editor.Get(a.ClientId));
        // Bob is not left in limbo: he gets his own bench back.
        Assert.NotNull(server.Editor.Get(b.ClientId));
        Assert.NotNull(SessionOf(b, b.ClientId));

        // A disconnect counts as leaving.
        b.Stop();
        TestClient.Pump(server, a, b);
        Assert.Null(server.Editor.Get(b.ClientId));
    }

    [Fact]
    public void LaunchEndsTheSessionAndSaysWhoIsAboard()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        OpenVab(server, a, a, b);
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);
        b.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = a.ClientId });
        TestClient.Pump(server, a, b);

        // Any builder may launch, guest included.
        b.Send(MessageId.EditorLaunch, new EditorLaunchMsg
        {
            Facility = EditorFacilityKind.Vab, ShipName = "Rocket", LaunchSite = "LaunchPad",
            AboardKerbals = new[] { "Alice Kerman", "Bob Kerman" }, SessionOwnerClientId = a.ClientId,
        });
        TestClient.Pump(server, a, b);

        var launch = a.Last<EditorLaunchMsg>();
        Assert.NotNull(launch);
        Assert.Equal("Rocket", launch!.Value.ShipName);
        Assert.Equal(b.ClientId, launch.Value.FromClientId);
        // Who is seated has to survive the relay: it is the only way the other player learns their kerbal is
        // going up, and the invite to join the flight is raised from it.
        Assert.Equal(new[] { "Alice Kerman", "Bob Kerman" }, launch.Value.AboardKerbals);
        Assert.False(server.Editor.Get(a.ClientId)!.HasCraft);
        Assert.Equal(0, server.Editor.Get(a.ClientId)!.Revision);
    }

    [Fact]
    public void BenchesCannotBeJoinedAcrossFacilities()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        OpenVab(server, a, a, b);
        a.Send(MessageId.EditorSnapshot, Snapshot("Rocket", 3, 0), Channel.Bulk);
        TestClient.Pump(server, a, b);

        b.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = EditorFacilityKind.Sph });
        TestClient.Pump(server, a, b);
        b.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = a.ClientId });
        TestClient.Pump(server, a, b);

        Assert.Equal(1, server.Editor.BuilderCount(a.ClientId));
        Assert.NotNull(server.Editor.Get(b.ClientId));
        Assert.Equal(EditorFacilityKind.Sph, server.Editor.Get(b.ClientId)!.Facility);
        Assert.Empty(b.Messages<EditorSnapshotMsg>());
    }
}
