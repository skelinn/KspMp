using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class ServerRosterTests
{
    private static string Kerbal(string name, string state = "Available", string trait = "Pilot") =>
        $"KERBAL\n{{\n\tname = {name}\n\tgender = Male\n\ttype = Crew\n\ttrait = {trait}\n\tbrave = 0.5\n\tdumb = 0.5\n\tbadS = False\n\tveteran = False\n\tstate = {state}\n\tinactive = False\n\tinactiveTimeEnd = 0\n\tgExperienced = 0\n\toutDueToG = False\n}}\n";

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
        return client;
    }

    [Fact]
    public void BootstrapRosterIsSharedAndSyncedInOrderBeforeVessels()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        Assert.True(a.Last<WelcomeMsg>()!.Value.NeedsAvatar);
        var sync = a.Last<SyncCompleteMsg>();
        Assert.NotNull(sync);
        Assert.Equal(0, sync!.Value.Kerbals);

        a.Send(MessageId.KerbalProto, new KerbalProtoMsg { Name = "Jebediah Kerman", Reason = KerbalReason.Bootstrap, NodeText = Kerbal("Jebediah Kerman") }, Channel.Bulk);
        a.Send(MessageId.KerbalProto, new KerbalProtoMsg { Name = "Bill Kerman", Reason = KerbalReason.Bootstrap, NodeText = Kerbal("Bill Kerman", "Assigned", "Engineer") }, Channel.Bulk);
        TestClient.Pump(server, a);
        Assert.Equal(2, server.Roster.Store.Count);

        var b = Join(hub, server, "Bob", a);
        var kerbals = b.Messages<KerbalProtoMsg>().ToList();
        Assert.Equal(new[] { "Jebediah Kerman", "Bill Kerman" }, kerbals.Select(k => k.Name));
        Assert.All(kerbals, k => Assert.False(k.IsAvatar));
        var order = b.Received.Select(r => r.Id).ToList();
        Assert.True(order.IndexOf(MessageId.KerbalProto) < order.IndexOf(MessageId.SyncComplete));
        Assert.Equal(2, b.Last<SyncCompleteMsg>()!.Value.Kerbals);
    }

    [Fact]
    public void AvatarClaimIsExclusiveAndProtectsTheKerbal()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        a.Send(MessageId.AvatarClaim, new AvatarClaimMsg { KerbalName = "Alice Kerman", Trait = "Scientist" });
        TestClient.Pump(server, a, b);
        var result = a.Last<AvatarClaimResultMsg>()!.Value;
        Assert.True(result.Ok);
        Assert.Equal("Scientist", result.Trait);
        Assert.Contains(b.Messages<PlayerListMsg>().Last().Players, p => p.Name == "Alice" && p.AvatarKerbalName == "Alice Kerman");

        a.Send(MessageId.KerbalProto, new KerbalProtoMsg { Name = "Alice Kerman", Reason = KerbalReason.Avatar, NodeText = Kerbal("Alice Kerman", "Available", "Scientist") }, Channel.Bulk);
        TestClient.Pump(server, a, b);
        var relayed = b.Messages<KerbalProtoMsg>().Last();
        Assert.True(relayed.IsAvatar);
        Assert.Equal(a.ClientId, relayed.AvatarClientId);
        Assert.Equal(a.PlayerId, relayed.AvatarPlayerId);

        b.Send(MessageId.AvatarClaim, new AvatarClaimMsg { KerbalName = "Alice Kerman", Trait = "Pilot" });
        TestClient.Pump(server, a, b);
        Assert.False(b.Last<AvatarClaimResultMsg>()!.Value.Ok);

        b.Send(MessageId.KerbalStatus, new KerbalStatusMsg { Name = "Alice Kerman", Status = 2 });
        b.Send(MessageId.KerbalRemoved, new KerbalRemovedMsg { Name = "Alice Kerman" });
        TestClient.Pump(server, a, b);
        Assert.True(server.Roster.Store.TryGet("Alice Kerman", out var record));
        Assert.Equal(0, record!.Status);
        Assert.Null(a.Last<KerbalStatusMsg>());

        a.Send(MessageId.KerbalStatus, new KerbalStatusMsg { Name = "Alice Kerman", Status = 1 });
        TestClient.Pump(server, a, b);
        Assert.Equal(1, b.Last<KerbalStatusMsg>()!.Value.Status);
        Assert.Contains("state = Assigned", server.Roster.Store.All.First().NodeText);
    }

    [Fact]
    public void AvatarAndPresenceSurviveReconnectAndRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kspmp-universe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hub1 = new LoopbackHub();
            var server1 = new ServerCore(hub1.CreateServer(), new ServerConfig(), new UniverseStore(dir), _ => { });
            server1.Start();
            var a = Join(hub1, server1, "Alice");
            a.Send(MessageId.AvatarClaim, new AvatarClaimMsg { KerbalName = "Alice Kerman", Trait = "Pilot" });
            a.Send(MessageId.KerbalProto, new KerbalProtoMsg { Name = "Alice Kerman", Reason = KerbalReason.Avatar, NodeText = Kerbal("Alice Kerman") }, Channel.Bulk);
            a.Send(MessageId.Presence, new PresenceMsg { State = PresenceState.InFlight, VesselId = Guid.NewGuid(), VesselName = "Kerbal X", Scene = 7 });
            TestClient.Pump(server1, a);
            var b = Join(hub1, server1, "Bob", a);
            var seen = b.Messages<PresenceMsg>().Last(p => p.ClientId == a.ClientId);
            Assert.Equal(PresenceState.InFlight, seen.State);
            Assert.Equal("Kerbal X", seen.VesselName);
            server1.Stop();
            Assert.True(File.Exists(Path.Combine(dir, "roster", "Alice Kerman.cfg")));

            var hub2 = new LoopbackHub();
            using var server2 = new ServerCore(hub2.CreateServer(), new ServerConfig(), new UniverseStore(dir), _ => { });
            server2.Start();
            var again = new TestClientWithId(hub2, "Alice", a.PlayerId);
            again.Start();
            TestClient.Pump(server2, again);
            var welcome = again.Last<WelcomeMsg>()!.Value;
            Assert.False(welcome.NeedsAvatar);
            Assert.Equal("Alice Kerman", welcome.AvatarKerbalName);
            Assert.True(again.Messages<KerbalProtoMsg>().Single().IsAvatar);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    /// <summary>A client that reconnects with a fixed player id.</summary>
    private sealed class TestClientWithId : TestClient
    {
        public TestClientWithId(LoopbackHub hub, string name, Guid playerId) : base(hub, name, playerId: playerId) { }
    }
}

public class AvatarClaimGuardTests
{
    private static string Kerbal(string name, string state) =>
        $"KERBAL\n{{\n\tname = {name}\n\tgender = Male\n\ttype = Crew\n\ttrait = Pilot\n\tbrave = 0.5\n\tdumb = 0.5\n\tbadS = False\n\tveteran = False\n\tstate = {state}\n\tinactive = False\n\tinactiveTimeEnd = 0\n}}\n";

    [Fact]
    public void AKerbalAlreadyFlyingCannotBeClaimed()
    {
        var hub = new LoopbackHub();
        using var server = new ServerCore(hub.CreateServer(), new ServerConfig(), new UniverseStore(null), _ => { });
        server.Start();
        var a = new TestClient(hub, "Alice");
        a.Start();
        TestClient.Pump(server, a);
        // Alice's rocket already has Jeb aboard.
        a.Send(MessageId.KerbalProto, new KerbalProtoMsg { Name = "Jebediah Kerman", Reason = KerbalReason.Bootstrap, NodeText = Kerbal("Jebediah Kerman", "Assigned") }, Channel.Bulk);
        a.Send(MessageId.KerbalProto, new KerbalProtoMsg { Name = "Bill Kerman", Reason = KerbalReason.Bootstrap, NodeText = Kerbal("Bill Kerman", "Available") }, Channel.Bulk);
        TestClient.Pump(server, a);

        var b = new TestClient(hub, "Bob");
        b.Start();
        TestClient.Pump(server, a, b);
        b.Send(MessageId.AvatarClaim, new AvatarClaimMsg { KerbalName = "Jebediah Kerman", Trait = "Pilot" });
        TestClient.Pump(server, a, b);
        var refused = b.Last<AvatarClaimResultMsg>()!.Value;
        Assert.False(refused.Ok);
        Assert.Contains("already assigned", refused.Reason);

        b.Send(MessageId.AvatarClaim, new AvatarClaimMsg { KerbalName = "Bill Kerman", Trait = "Engineer" });
        TestClient.Pump(server, a, b);
        Assert.True(b.Messages<AvatarClaimResultMsg>().Last().Ok);
    }
}
