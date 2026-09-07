using System.Text;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class ServerDockingTests
{
    private static byte[] Deflate(string text) => Encoding.UTF8.GetBytes(text) is var raw ? DeflateCodec.Compress(raw, 0, raw.Length) : throw new InvalidOperationException();

    private static VesselProtoMsg Proto(Guid id, uint persistentId, string name, string? crew) => new()
    {
        VesselId = id, PersistentId = persistentId, Name = name, VesselType = "Ship", Reason = ProtoReason.FlightReady,
        ProtoDeflated = Deflate($"pid = {id:N}\npersistentId = {persistentId}\nname = {name}\nref = 1\nPART\n{{\n\tuid = 1\n" + (crew != null ? $"\tcrew = {crew}\n" : "") + "\tMODULE\n\t{\n\t\tname = ModuleCommand\n\t}\n}\n"),
    };

    private static ServerCore NewServer(LoopbackHub hub)
    {
        var server = new ServerCore(hub.CreateServer(), new ServerConfig(), new UniverseStore(null), _ => { });
        server.Start();
        return server;
    }

    private static TestClient JoinWithAvatar(LoopbackHub hub, ServerCore server, string player, string kerbal, params TestClient[] others)
    {
        var client = new TestClient(hub, player);
        client.Start();
        TestClient.Pump(server, others.Append(client).ToArray());
        client.Send(MessageId.AvatarClaim, new AvatarClaimMsg { KerbalName = kerbal, Trait = "Pilot" });
        TestClient.Pump(server, others.Append(client).ToArray());
        return client;
    }

    [Fact]
    public void UnpilotedVesselYieldsToTheApproachingPilotAndDockingMergesTheRecords()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);
        var ship = Guid.NewGuid();
        var station = Guid.NewGuid();
        alice.Send(MessageId.VesselProto, Proto(ship, 100, "Ship", "Alice Kerman"), Channel.Bulk);
        bob.Send(MessageId.VesselProto, Proto(station, 200, "Station", null), Channel.Bulk); // Bob controls an uncrewed station from mission control
        TestClient.Pump(server, alice, bob);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(station));

        alice.Send(MessageId.DockIntent, new DockIntentMsg { MyVesselId = ship, OtherVesselId = station, DistanceMeters = 30 });
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(station));
        Assert.True(server.Authority.IsDockingHeld(station));

        // Bob's station snapshot arriving now does not hand it back (no pilot aboard anyway), and a pilot rule is suspended by the hold.
        alice.Send(MessageId.DockCommit, new DockCommitMsg { SurvivorVesselId = ship, RemovedVesselId = station, Name = "Ship + Station", ProtoDeflated = Proto(ship, 100, "Ship + Station", "Alice Kerman").ProtoDeflated }, Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        Assert.Equal(1, server.Vessels.Count);
        Assert.True(server.Vessels.TryGet(ship, out var merged));
        Assert.Equal("Ship + Station", merged!.Name);
        Assert.True(server.Authority.IsUnowned(station));
        var commit = bob.Last<DockCommitMsg>();
        Assert.NotNull(commit);
        Assert.Equal(station, commit!.Value.RemovedVesselId);
        Assert.Equal(alice.ClientId, commit.Value.OwnerClientId);
        // Everyone also hears the absorbed vessel is gone, in a message understood in every scene.
        var removal = bob.Last<VesselRemoveMsg>();
        Assert.NotNull(removal);
        Assert.Equal(station, removal!.Value.VesselId);
        Assert.Null(alice.Last<VesselRemoveMsg>());
    }

    [Fact]
    public void WhoeverDocksOwnsTheMergedVesselEvenWhenTheOtherPlayerOwnedTheSurvivor()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);
        var ship = Guid.NewGuid();
        var station = Guid.NewGuid();
        alice.Send(MessageId.VesselProto, Proto(ship, 100, "Ship", "Alice Kerman"), Channel.Bulk);
        bob.Send(MessageId.VesselProto, Proto(station, 200, "Station", "Bob Kerman"), Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        var seqBefore = server.Authority.SeqOf(station);

        // KSP on Alice's machine kept Bob's station as the survivor; the hold never landed. Her commit still counts.
        alice.Send(MessageId.DockCommit, new DockCommitMsg { SurvivorVesselId = station, RemovedVesselId = ship, Name = "Station + Ship", ProtoDeflated = Proto(station, 200, "Station + Ship", "Bob Kerman").ProtoDeflated }, Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        Assert.Equal(1, server.Vessels.Count);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(station));
        Assert.True(server.Authority.SeqOf(station) > seqBefore);
        var commit = bob.Last<DockCommitMsg>();
        Assert.NotNull(commit);
        Assert.Equal(alice.ClientId, commit!.Value.OwnerClientId);
        Assert.Equal(server.Authority.SeqOf(station), commit.Value.AuthoritySeq);

        // A player who owns neither cannot report a docking at all.
        var carol = JoinWithAvatar(hub, server, "Carol", "Carol Kerman", alice, bob);
        var other = Guid.NewGuid();
        carol.Send(MessageId.DockCommit, new DockCommitMsg { SurvivorVesselId = station, RemovedVesselId = other, Name = "x", ProtoDeflated = Proto(station, 200, "x", null).ProtoDeflated }, Channel.Bulk);
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(station));
        Assert.Equal("Station + Ship", server.Vessels.TryGet(station, out var record) ? record!.Name : null);
    }

    [Fact]
    public void WithTwoPilotsTheLowerPersistentIdYieldsAndOnlyAskingGetsItBack()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        server.Authority.DockingHoldSeconds = 0;
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        alice.Send(MessageId.VesselProto, Proto(a, 500, "A", "Alice Kerman"), Channel.Bulk);
        bob.Send(MessageId.VesselProto, Proto(b, 400, "B", "Bob Kerman"), Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        Fly(server, alice, a, alice, bob);
        Fly(server, bob, b, alice, bob);

        alice.Send(MessageId.DockIntent, new DockIntentMsg { MyVesselId = a, OtherVesselId = b, DistanceMeters = 40 });
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(b)); // B has the lower persistent id: it yields to Alice
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(a));

        // The hold has expired, but nothing hands B back on its own any more. The seat rule used to do that, and
        // it was wrong in both directions: it gave craft to players who were not there, and it pulled authority
        // back mid-approach. Bob is still aboard B and still flying it, so he asks, and gets it.
        bob.Send(MessageId.VesselProto, Proto(b, 400, "B", "Bob Kerman"), Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(b));

        bob.Send(MessageId.ControlRequest, new ControlRequestMsg { VesselId = b });
        TestClient.Pump(server, alice, bob);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(b));
        Assert.Equal(AuthorityReason.HandedOver, bob.Messages<AuthorityAssignMsg>().Last().Reason);
    }

    /// <summary>Says "I am in the flight scene, on this vessel" - the precondition for owning one.</summary>
    private static void Fly(ServerCore server, TestClient client, Guid vesselId, params TestClient[] all)
    {
        client.Send(MessageId.Presence, new PresenceMsg { State = PresenceState.InFlight, VesselId = vesselId, VesselName = "v", Scene = 7 });
        TestClient.Pump(server, all);
    }
}
