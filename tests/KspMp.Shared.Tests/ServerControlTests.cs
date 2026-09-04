using System.Text;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Server.Vessels;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class ServerControlTests
{
    private static readonly Guid VesselId = Guid.NewGuid();

    private static string VesselText(params (uint uid, bool command, string[] crew)[] parts)
    {
        var sb = new StringBuilder();
        sb.Append("pid = ").Append(VesselId.ToString("N")).Append("\nname = Two Seater\ntype = Ship\nref = ").Append(parts[0].uid).Append('\n');
        foreach (var (uid, command, crew) in parts)
        {
            sb.Append("PART\n{\n\tname = pod\n\tuid = ").Append(uid).Append('\n');
            foreach (var c in crew) sb.Append("\tcrew = ").Append(c).Append('\n');
            if (command) sb.Append("\tMODULE\n\t{\n\t\tname = ModuleCommand\n\t}\n");
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    private static VesselProtoMsg Proto(string text) =>
        Encoding.UTF8.GetBytes(text) is var raw
            ? new VesselProtoMsg { VesselId = VesselId, Name = "Two Seater", VesselType = "Ship", Reason = ProtoReason.FlightReady, ProtoDeflated = DeflateCodec.Compress(raw, 0, raw.Length) }
            : throw new InvalidOperationException();

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
        Assert.True(client.Last<AvatarClaimResultMsg>()!.Value.Ok);
        return client;
    }

    [Fact]
    public void CrewInfoFindsTheCommandSeat()
    {
        var info = VesselCrewInfo.Parse(VesselText((10, true, new[] { "Jebediah Kerman", "Alice Kerman" }), (11, false, new[] { "Bob Kerman" })));
        Assert.Equal(10u, info.ReferencePartId);
        Assert.Equal(new[] { "Jebediah Kerman", "Alice Kerman", "Bob Kerman" }, info.AllCrew());
        Assert.Equal("Alice Kerman", info.CommandSeatOccupant(n => n != "Jebediah Kerman")); // Jeb is an NPC: first avatar in a command part
        Assert.Equal("Jebediah Kerman", info.CommandSeatOccupant(_ => true));
        Assert.Null(info.CommandSeatOccupant(_ => false));
    }

    [Fact]
    public void PilotBySeatOwnsPhysicsAndCoPilotInputIsForwardedToThePilot()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);
        var carol = JoinWithAvatar(hub, server, "Carol", "Carol Kerman", alice, bob);

        // Bob launches the vessel, but Alice sits in the command seat: Alice becomes pilot and physics owner.
        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        TestClient.Pump(server, alice, bob, carol);
        var roles = carol.Last<VesselRolesMsg>()!.Value;
        Assert.Equal(alice.ClientId, roles.PilotClientId);
        Assert.Equal(new[] { alice.ClientId, bob.ClientId }, roles.AboardClientIds);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));
        Assert.Equal(alice.ClientId, alice.Messages<AuthorityAssignMsg>().Last().OwnerClientId);

        // Bob (co-pilot) sends input: forwarded to Alice only. Carol (not aboard) is dropped.
        bob.Send(MessageId.CtrlInput, new CtrlInputMsg { VesselId = VesselId, Seq = 1, Active = CtrlAxes.Pitch, Pitch = 0.5f }, Channel.State, Delivery.Sequenced);
        carol.Send(MessageId.CtrlInput, new CtrlInputMsg { VesselId = VesselId, Seq = 1, Active = CtrlAxes.Roll, Roll = 1f }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, alice, bob, carol);
        var inputs = alice.Messages<CtrlInputMsg>().ToList();
        Assert.Single(inputs);
        Assert.Equal(bob.ClientId, inputs[0].FromClientId);
        Assert.Equal(0.5f, inputs[0].Pitch);

        // Alice's merged state reaches Bob (aboard) but not Carol.
        alice.Send(MessageId.CtrlState, new CtrlInputMsg { VesselId = VesselId, Seq = 2, MainThrottle = 1f }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(1f, bob.Messages<CtrlInputMsg>().Last().MainThrottle);
        Assert.Empty(carol.Messages<CtrlInputMsg>());

        // Bob stages: forwarded to Alice.
        bob.Send(MessageId.Stage, new StageMsg { VesselId = VesselId });
        bob.Send(MessageId.PartEvent, new PartEventMsg { VesselId = VesselId, PartFlightId = 10, ModuleIndex = 0, EventName = "Deploy" });
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(bob.ClientId, alice.Last<StageMsg>()!.Value.FromClientId);
        Assert.Equal("Deploy", alice.Last<PartEventMsg>()!.Value.EventName);

        // Alice leaves: Bob, next in the command part, becomes pilot and takes over the physics.
        alice.Stop();
        TestClient.Pump(server, alice, bob, carol);
        var after = bob.Messages<VesselRolesMsg>().Last();
        Assert.Equal(bob.ClientId, after.PilotClientId);
        Assert.Equal(new[] { bob.ClientId }, after.AboardClientIds);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));
        Assert.Equal(bob.ClientId, bob.Messages<AuthorityAssignMsg>().Last().OwnerClientId);
    }
}
