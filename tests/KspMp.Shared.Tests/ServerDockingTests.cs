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

    private static VesselProtoMsg Proto(Guid id, uint persistentId, string name, string crew) => new()
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
    }

    [Fact]
    public void WithTwoPilotsTheLowerPersistentIdYieldsAndTheHoldExpires()
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

        alice.Send(MessageId.DockIntent, new DockIntentMsg { MyVesselId = a, OtherVesselId = b, DistanceMeters = 40 });
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(b)); // B has the lower persistent id: it yields to Alice
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(a));

        // Hold expired (0 s): Bob's next snapshot puts the seat rule back in charge of B.
        bob.Send(MessageId.VesselProto, Proto(b, 400, "B", "Bob Kerman"), Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(b)); // snapshot from a non-owner is ignored...
        server.Control.OnVesselSnapshot(server.Vessels.All.First(v => v.Id == b));
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(b)); // ...but the seat rule reassigns once the hold is gone
    }
}
