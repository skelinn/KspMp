using System.Text;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

/// <summary>
/// Climbing in and out of other people's craft. Only the client simulating a vessel may change its crew, so the
/// player who pressed the button sends what they did and the server forwards it to the owner - after checking
/// that the kerbal is really theirs, since otherwise anyone could throw anyone out of an airlock.
/// </summary>
public class ServerCrewTests
{
    private static readonly Guid VesselId = Guid.NewGuid();

    private static string VesselText(params string[] crew)
    {
        var sb = new StringBuilder();
        sb.Append("pid = ").Append(VesselId.ToString("N")).Append("\nname = Lander\ntype = Ship\nref = 10\n");
        sb.Append("PART\n{\n\tname = pod\n\tuid = 10\n");
        foreach (var c in crew) sb.Append("\tcrew = ").Append(c).Append('\n');
        sb.Append("\tMODULE\n\t{\n\t\tname = ModuleCommand\n\t}\n}\n");
        return sb.ToString();
    }

    private static VesselProtoMsg Proto(string text) =>
        Encoding.UTF8.GetBytes(text) is var raw
            ? new VesselProtoMsg { VesselId = VesselId, Name = "Lander", VesselType = "Ship", Reason = ProtoReason.FlightReady, ProtoDeflated = DeflateCodec.Compress(raw, 0, raw.Length) }
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

    private static void Fly(ServerCore server, TestClient client, Guid vesselId, params TestClient[] all)
    {
        client.Send(MessageId.Presence, new PresenceMsg { State = PresenceState.InFlight, VesselId = vesselId, VesselName = "Lander", Scene = 7 });
        TestClient.Pump(server, all);
    }

    [Fact]
    public void EvaAndBoardingAreForwardedToWhoeverSimulatesTheVessel()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        alice.Send(MessageId.VesselProto, Proto(VesselText("Alice Kerman", "Bob Kerman")), Channel.Bulk);
        Fly(server, alice, VesselId, alice, bob);
        Fly(server, bob, VesselId, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));

        var eva = Guid.NewGuid();
        bob.Send(MessageId.CrewEva, new CrewEvaMsg { FromVesselId = VesselId, KerbalName = "Bob Kerman", EvaVesselId = eva });
        TestClient.Pump(server, alice, bob);
        var out_ = alice.Last<CrewEvaMsg>();
        Assert.NotNull(out_);
        Assert.Equal("Bob Kerman", out_!.Value.KerbalName);
        Assert.Equal(bob.ClientId, out_.Value.FromClientId);
        Assert.Equal(eva, out_.Value.EvaVesselId);

        bob.Send(MessageId.CrewBoard, new CrewBoardMsg { ToVesselId = VesselId, PartFlightId = 10, SeatIndex = -1, KerbalName = "Bob Kerman", EvaVesselId = eva });
        TestClient.Pump(server, alice, bob);
        var back = alice.Last<CrewBoardMsg>();
        Assert.NotNull(back);
        Assert.Equal(10u, back!.Value.PartFlightId);
        Assert.Equal(bob.ClientId, back.Value.FromClientId);
    }

    [Fact]
    public void NobodyCanMoveSomebodyElsesKerbal()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        alice.Send(MessageId.VesselProto, Proto(VesselText("Alice Kerman", "Bob Kerman")), Channel.Bulk);
        Fly(server, alice, VesselId, alice, bob);

        // Bob tries to put Alice's kerbal out of the airlock.
        bob.Send(MessageId.CrewEva, new CrewEvaMsg { FromVesselId = VesselId, KerbalName = "Alice Kerman", EvaVesselId = Guid.NewGuid() });
        TestClient.Pump(server, alice, bob);
        Assert.Empty(alice.Messages<CrewEvaMsg>());
    }

    /// <summary>
    /// The owner stepping out while a co-pilot is aboard and flying it: the rocket goes to the co-pilot rather
    /// than to nobody. This is P1's departure rule reached through the door EVA opens.
    /// </summary>
    [Fact]
    public void APilotWhoEvasHandsTheRocketToACoPilotWhoIsFlyingIt()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        alice.Send(MessageId.VesselProto, Proto(VesselText("Alice Kerman", "Bob Kerman")), Channel.Bulk);
        Fly(server, alice, VesselId, alice, bob);
        Fly(server, bob, VesselId, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));

        // Alice climbs out; the snapshot that follows no longer has her aboard.
        alice.Send(MessageId.VesselProto, Proto(VesselText("Bob Kerman")), Channel.Bulk);
        TestClient.Pump(server, alice, bob);

        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));
        Assert.Equal(AuthorityReason.PilotLeft, bob.Messages<AuthorityAssignMsg>().Last().Reason);
    }
}
