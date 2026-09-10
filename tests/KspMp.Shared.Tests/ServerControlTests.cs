using System.Text;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Server.Vessels;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

/// <summary>
/// The control rules R1-R5 documented on <see cref="ControlService"/>. The theme running through them: the
/// server never makes somebody the physics owner of a vessel they are not actually flying, because that leaves
/// nobody simulating it - the failure recorded in docs/PLAN.md that these rules exist to prevent.
/// </summary>
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

    /// <summary>Says "I am in the flight scene, on this vessel" - the precondition for owning it.</summary>
    private static void Fly(ServerCore server, TestClient client, Guid vesselId, params TestClient[] all)
    {
        client.Send(MessageId.Presence, new PresenceMsg { State = PresenceState.InFlight, VesselId = vesselId, VesselName = "Two Seater", Scene = (byte)7 });
        TestClient.Pump(server, all.Length > 0 ? all : new[] { client });
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

    /// <summary>
    /// R1. The bug this replaces: the host seated his friend's kerbal in the command seat, launched, and the
    /// server handed the rocket to the friend - who was still in the VAB. Nobody simulated it and the host was
    /// locked out of his own controls.
    /// </summary>
    [Fact]
    public void LauncherIsPilotAndOwnerEvenWhenAFriendSitsInTheCommandSeat()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        // Bob launches; Alice's kerbal is in the command seat, but Alice is not in the flight at all.
        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob);

        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));
        var roles = alice.Last<VesselRolesMsg>()!.Value;
        Assert.Equal(bob.ClientId, roles.PilotClientId);
        Assert.Equal(new[] { alice.ClientId, bob.ClientId }, roles.AboardClientIds);
    }

    [Fact]
    public void CoPilotInputAndDiscreteActionsGoToTheOwner()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);
        var carol = JoinWithAvatar(hub, server, "Carol", "Carol Kerman", alice, bob);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob, carol);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));

        // Alice (co-pilot) sends input: forwarded to Bob only. Carol (not aboard) is dropped.
        alice.Send(MessageId.CtrlInput, new CtrlInputMsg { VesselId = VesselId, Seq = 1, Active = CtrlAxes.Pitch, Pitch = 0.5f }, Channel.State, Delivery.Sequenced);
        carol.Send(MessageId.CtrlInput, new CtrlInputMsg { VesselId = VesselId, Seq = 1, Active = CtrlAxes.Roll, Roll = 1f }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, alice, bob, carol);
        var inputs = bob.Messages<CtrlInputMsg>().ToList();
        Assert.Single(inputs);
        Assert.Equal(alice.ClientId, inputs[0].FromClientId);
        Assert.Equal(0.5f, inputs[0].Pitch);

        // The owner's merged state reaches Alice (aboard) but not Carol.
        bob.Send(MessageId.CtrlState, new CtrlInputMsg { VesselId = VesselId, Seq = 2, MainThrottle = 1f }, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(1f, alice.Messages<CtrlInputMsg>().Last().MainThrottle);
        Assert.Empty(carol.Messages<CtrlInputMsg>());

        // Staging and part buttons from a co-pilot reach the owner too.
        alice.Send(MessageId.Stage, new StageMsg { VesselId = VesselId });
        alice.Send(MessageId.PartEvent, new PartEventMsg { VesselId = VesselId, PartFlightId = 10, ModuleIndex = 0, EventName = "Deploy" });
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(alice.ClientId, bob.Last<StageMsg>()!.Value.FromClientId);
        Assert.Equal("Deploy", bob.Last<PartEventMsg>()!.Value.EventName);
    }

    /// <summary>R2, and the sequence number that makes a stale snapshot harmless.</summary>
    [Fact]
    public void ControlIsGivenOnlyToSomeoneFlyingTheVessel()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob);
        var seqAtLaunch = server.Authority.SeqOf(VesselId);

        // Alice is aboard on paper but has not reached the flight scene: refused, and nothing moves.
        bob.Send(MessageId.ControlGive, new ControlGiveMsg { VesselId = VesselId, ToClientId = alice.ClientId });
        TestClient.Pump(server, alice, bob);
        Assert.Equal(AuthorityReason.NotInFlight, bob.Messages<AuthorityAssignMsg>().Last().Reason);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));
        Assert.Equal(seqAtLaunch, server.Authority.SeqOf(VesselId));

        // Once Alice is actually flying it, the same request works.
        Fly(server, alice, VesselId, alice, bob);
        bob.Send(MessageId.ControlGive, new ControlGiveMsg { VesselId = VesselId, ToClientId = alice.ClientId });
        TestClient.Pump(server, alice, bob);
        var granted = alice.Messages<AuthorityAssignMsg>().Last();
        Assert.Equal(AuthorityReason.HandedOver, granted.Reason);
        Assert.Equal(alice.ClientId, granted.OwnerClientId);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));
        Assert.True(granted.AuthoritySeq > seqAtLaunch, "every decision must advance the sequence");

        // The pilot follows the owner, so Alice is announced as pilot too.
        Assert.Equal(alice.ClientId, bob.Messages<VesselRolesMsg>().Last().PilotClientId);
    }

    [Fact]
    public void RequestIsRelayedToThePilotAndDeclineComesBack()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob);
        Fly(server, alice, VesselId, alice, bob);

        alice.Send(MessageId.ControlRequest, new ControlRequestMsg { VesselId = VesselId });
        TestClient.Pump(server, alice, bob);
        var asked = bob.Last<ControlRequestMsg>();
        Assert.NotNull(asked);
        Assert.Equal(alice.ClientId, asked!.Value.FromClientId);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));   // asking alone changes nothing

        bob.Send(MessageId.ControlDecline, new ControlDeclineMsg { VesselId = VesselId, ToClientId = alice.ClientId });
        TestClient.Pump(server, alice, bob);
        Assert.Equal(bob.ClientId, alice.Last<ControlDeclineMsg>()!.Value.FromClientId);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));
    }

    /// <summary>R3: when the owner is not there to answer, the asker gets it rather than waiting forever.</summary>
    [Fact]
    public void RequestIsGrantedAtOnceWhenTheOwnerIsNotFlyingIt()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        TestClient.Pump(server, alice, bob);   // Bob owns it by R1 but never reports being in flight

        Fly(server, alice, VesselId, alice, bob);
        alice.Send(MessageId.ControlRequest, new ControlRequestMsg { VesselId = VesselId });
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));
        Assert.Equal(AuthorityReason.HandedOver, alice.Messages<AuthorityAssignMsg>().Last().Reason);
    }

    /// <summary>R4 on a disconnect.</summary>
    [Fact]
    public void WhenThePilotDisconnectsWhoeverIsAboardAndFlyingTakesOver()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob);
        Fly(server, alice, VesselId, alice, bob);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));

        bob.Stop();
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));
        var taken = alice.Messages<AuthorityAssignMsg>().Last();
        Assert.Equal(AuthorityReason.PilotLeft, taken.Reason);
        Assert.Equal(alice.ClientId, taken.OwnerClientId);
    }

    /// <summary>R5: with nobody flying it the vessel goes unowned, and is then there for the asking.</summary>
    [Fact]
    public void NobodyWhoIsNotFlyingIsMadeOwner()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob);

        bob.Stop();   // Alice is aboard but has never been in the flight
        TestClient.Pump(server, alice, bob);
        Assert.Equal(0, server.Authority.OwnerOf(VesselId));

        Fly(server, alice, VesselId, alice);
        alice.Send(MessageId.AuthorityRequest, new AuthorityRequestMsg { VesselId = VesselId });
        TestClient.Pump(server, alice);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));
    }

    /// <summary>
    /// R4 on a departure. A pilot who steps out onto EVA with nobody else flying the rocket keeps simulating it -
    /// releasing it there would leave the rocket beside them unsimulated.
    /// </summary>
    [Fact]
    public void PilotWhoLeavesTheVesselHandsOverOnlyToSomeoneFlyingIt()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));

        // Bob's kerbal leaves the craft and Alice is not flying it: Bob keeps it.
        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman" }))), Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));

        // Alice reaches the flight; the next snapshot without Bob aboard hands it to her.
        Fly(server, alice, VesselId, alice, bob);
        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman" }))), Channel.Bulk);
        TestClient.Pump(server, alice, bob);
        Assert.Equal(alice.ClientId, server.Authority.OwnerOf(VesselId));
        Assert.Equal(AuthorityReason.PilotLeft, alice.Messages<AuthorityAssignMsg>().Last().Reason);
    }

    [Fact]
    public void SharedStickIsPerVesselAndOnlyThePilotSetsIt()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);

        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Alice Kerman", "Bob Kerman" }))), Channel.Bulk);
        Fly(server, bob, VesselId, alice, bob);
        Assert.False(alice.Messages<VesselRolesMsg>().Last().SharedStick);

        // The co-pilot cannot let herself steer.
        alice.Send(MessageId.ControlSetSharedStick, new ControlSetSharedStickMsg { VesselId = VesselId, Enabled = true });
        TestClient.Pump(server, alice, bob);
        Assert.False(alice.Messages<VesselRolesMsg>().Last().SharedStick);

        bob.Send(MessageId.ControlSetSharedStick, new ControlSetSharedStickMsg { VesselId = VesselId, Enabled = true });
        TestClient.Pump(server, alice, bob);
        Assert.True(alice.Messages<VesselRolesMsg>().Last().SharedStick);

        bob.Send(MessageId.ControlSetSharedStick, new ControlSetSharedStickMsg { VesselId = VesselId, Enabled = false });
        TestClient.Pump(server, alice, bob);
        Assert.False(alice.Messages<VesselRolesMsg>().Last().SharedStick);
    }

    [Fact]
    public void ThePilotsOwnActionsReachEveryoneAboardAndNobodyElse()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var alice = JoinWithAvatar(hub, server, "Alice", "Alice Kerman");
        var bob = JoinWithAvatar(hub, server, "Bob", "Bob Kerman", alice);
        var carol = JoinWithAvatar(hub, server, "Carol", "Carol Kerman", alice, bob);

        // Bob launches with Alice aboard; Carol is elsewhere.
        bob.Send(MessageId.VesselProto, Proto(VesselText((10, true, new[] { "Bob Kerman", "Alice Kerman" }))), Channel.Bulk);
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(bob.ClientId, server.Authority.OwnerOf(VesselId));

        // The pilot stages, presses a part button and toggles a group: Alice mirrors all three, Carol hears nothing,
        // and nothing comes back to Bob himself.
        bob.Send(MessageId.Stage, new StageMsg { VesselId = VesselId });
        bob.Send(MessageId.PartEvent, new PartEventMsg { VesselId = VesselId, PartFlightId = 10, ModuleIndex = 0, EventName = "Deploy" });
        bob.Send(MessageId.ActionGroup, new ActionGroupMsg { VesselId = VesselId, Group = 1, Toggle = true });
        bob.Send(MessageId.PartField, new PartFieldMsg { VesselId = VesselId, PartFlightId = 10, ModuleIndex = 0, FieldName = "thrustPercentage", Value = "50" });
        bob.Send(MessageId.StageSequence, new StageSequenceMsg { VesselId = VesselId, CurrentStage = 3, PartFlightIds = new uint[] { 10 }, Stages = new[] { 2 } });
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(2, alice.Last<StageSequenceMsg>()!.Value.Stages[0]);
        Assert.Equal(3, alice.Last<StageSequenceMsg>()!.Value.CurrentStage);
        Assert.Empty(carol.Messages<StageSequenceMsg>());
        Assert.Equal(bob.ClientId, alice.Last<StageMsg>()!.Value.FromClientId);
        Assert.Equal("Deploy", alice.Last<PartEventMsg>()!.Value.EventName);
        Assert.Equal("50", alice.Last<PartFieldMsg>()!.Value.Value);
        Assert.Empty(carol.Messages<PartFieldMsg>());
        Assert.Equal(1, alice.Last<ActionGroupMsg>()!.Value.Group);
        Assert.Empty(carol.Messages<StageMsg>());
        Assert.Empty(carol.Messages<PartEventMsg>());
        Assert.Empty(bob.Messages<StageMsg>());

        // So do the pilot's tank levels, and only the pilot's: Alice's own would be ignored.
        var fuel = new VesselResourcesMsg
        {
            VesselId = VesselId,
            Parts = new[] { new VesselResourcesMsg.PartResources { PartFlightId = 10, Resources = new[] { new VesselResourcesMsg.Resource { Name = "LiquidFuel", Amount = 123.5f } } } },
        };
        bob.Send(MessageId.VesselResources, fuel, Channel.Bulk);
        alice.Send(MessageId.VesselResources, fuel, Channel.Bulk);
        TestClient.Pump(server, alice, bob, carol);
        var got = alice.Last<VesselResourcesMsg>()!.Value;
        Assert.Equal(10u, got.Parts[0].PartFlightId);
        Assert.Equal("LiquidFuel", got.Parts[0].Resources[0].Name);
        Assert.Equal(123.5f, got.Parts[0].Resources[0].Amount);
        Assert.Empty(carol.Messages<VesselResourcesMsg>());
        Assert.Empty(bob.Messages<VesselResourcesMsg>());

        // The stage carries its index, so the mirror fires the same one.
        bob.Send(MessageId.Stage, new StageMsg { VesselId = VesselId, Stage = 3 });
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal(3, alice.Last<StageMsg>()!.Value.Stage);

        // A kerbal's jetpack goes to everyone who can see it, from its owner only.
        var fx = new EvaFxMsg { VesselId = VesselId, Flags = EvaFxMsg.JetpackDeployed | EvaFxMsg.HasFuel, LinY = 100 };
        bob.Send(MessageId.EvaFx, fx, Channel.State, Delivery.Sequenced);
        alice.Send(MessageId.EvaFx, fx, Channel.State, Delivery.Sequenced);
        TestClient.Pump(server, alice, bob, carol);
        Assert.Equal((sbyte)100, alice.Last<EvaFxMsg>()!.Value.LinY);
        Assert.Equal((sbyte)100, carol.Last<EvaFxMsg>()!.Value.LinY);
        Assert.Empty(bob.Messages<EvaFxMsg>());
    }
}
