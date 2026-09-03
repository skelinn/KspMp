using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class ServerLobbyTests
{
    private static ServerCore NewServer(LoopbackHub hub, ServerConfig? config = null, UniverseStore? universe = null)
    {
        var server = new ServerCore(hub.CreateServer(), config ?? new ServerConfig { ServerName = "Test" }, universe ?? new UniverseStore(null), _ => { });
        server.Start();
        return server;
    }

    [Fact]
    public void TwoClientsSeeEachOtherJoinAndLeave()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = new TestClient(hub, "Alice");
        var b = new TestClient(hub, "Bob");

        a.Start();
        TestClient.Pump(server, a, b);
        var welcomeA = a.Last<WelcomeMsg>();
        Assert.NotNull(welcomeA);
        Assert.Equal("Test", welcomeA!.Value.ServerName);
        Assert.Equal(new[] { "Alice" }, a.Last<PlayerListMsg>()!.Value.Players.Select(p => p.Name));

        b.Start();
        TestClient.Pump(server, a, b);
        Assert.Equal(new[] { "Alice", "Bob" }, b.Last<PlayerListMsg>()!.Value.Players.Select(p => p.Name));
        Assert.Equal("Bob", a.Last<PlayerJoinedMsg>()!.Value.Player.Name);
        Assert.Contains(a.Messages<ChatMsg>(), m => m.FromClientId == 0 && m.Text == "Bob joined");
        Assert.Equal(2, server.OnlineCount);

        b.Stop();
        TestClient.Pump(server, a, b);
        Assert.Equal("Bob", a.Last<PlayerLeftMsg>()!.Value.Name);
        Assert.Equal(1, server.OnlineCount);
    }

    [Fact]
    public void ChatIsRelayedToEveryoneWithTheSenderFilledIn()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = new TestClient(hub, "Alice");
        var b = new TestClient(hub, "Bob");
        a.Start();
        b.Start();
        TestClient.Pump(server, a, b);

        a.Send(MessageId.Chat, new ChatMsg { FromClientId = 999, FromName = "spoofed", Text = "  hello there  " }, Channel.ChatMod);
        TestClient.Pump(server, a, b);

        foreach (var client in new[] { a, b })
        {
            var chat = client.Messages<ChatMsg>().Last();
            Assert.Equal(a.ClientId, chat.FromClientId);
            Assert.Equal("Alice", chat.FromName);
            Assert.Equal("hello there", chat.Text);
        }
    }

    [Fact]
    public async Task TimeSyncEchoesTheRequestAndAdvances()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub, new ServerConfig { InitialUniversalTime = 1000 });
        var a = new TestClient(hub, "Alice");
        a.Start();
        TestClient.Pump(server, a);

        a.Send(MessageId.TimeSyncReq, new TimeSyncReqMsg { ClientTicks = 42 }, Channel.State, Delivery.Unreliable);
        TestClient.Pump(server, a);
        var first = a.Messages<TimeSyncMsg>().Last(m => m.ClientTicks == 42);
        Assert.True(first.UniversalTime >= 1000);
        Assert.Equal(1f, first.Rate);

        await Task.Delay(60);
        a.Send(MessageId.TimeSyncReq, new TimeSyncReqMsg { ClientTicks = 43 }, Channel.State, Delivery.Unreliable);
        TestClient.Pump(server, a);
        var second = a.Messages<TimeSyncMsg>().Last(m => m.ClientTicks == 43);
        Assert.True(second.UniversalTime > first.UniversalTime + 0.04, $"UT did not advance: {first.UniversalTime} -> {second.UniversalTime}");
    }

    [Fact]
    public void FullServerAndDuplicateIdsAreRejected()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub, new ServerConfig { MaxPlayers = 1 });
        server.RejectGraceMs = 0;
        var a = new TestClient(hub, "Alice");
        var b = new TestClient(hub, "Bob");
        a.Start();
        TestClient.Pump(server, a);
        b.Start();
        TestClient.Pump(server, a, b);

        Assert.Contains("full", b.Last<RejectMsg>()!.Value.Reason);
        Assert.True(b.Disconnected);
        Assert.Equal(1, server.OnlineCount);
    }

    [Fact]
    public void ProtocolMismatchIsRejectedBeforeAnyState()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        server.RejectGraceMs = 0;
        var old = new TestClient(hub, "Old", protocolVersion: 999);
        old.Start();
        TestClient.Pump(server, old);

        Assert.Contains("Protocol version mismatch", old.Last<RejectMsg>()!.Value.Reason);
        Assert.True(old.Disconnected);
        Assert.Null(old.Last<WelcomeMsg>());
        Assert.Equal(0, server.OnlineCount);
    }

    [Fact]
    public void UniversePersistsTimeAndPlayersAcrossRestarts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kspmp-universe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hub1 = new LoopbackHub();
            var server1 = new ServerCore(hub1.CreateServer(), new ServerConfig { InitialUniversalTime = 500 }, new UniverseStore(dir), _ => { });
            server1.Start();
            var a = new TestClient(hub1, "Alice");
            a.Start();
            TestClient.Pump(server1, a);
            server1.Time.SetUniversalTime(2500);
            server1.Stop();

            Assert.True(File.Exists(Path.Combine(dir, "time.cfg")));
            Assert.True(File.Exists(Path.Combine(dir, "players.cfg")));

            var hub2 = new LoopbackHub();
            using var server2 = new ServerCore(hub2.CreateServer(), ServerConfig.Load(dir), new UniverseStore(dir), _ => { });
            Assert.True(server2.Time.UniversalTime >= 2500);
            Assert.Single(server2.KnownPlayers);
            Assert.Equal("Alice", server2.KnownPlayers.First().Name);
            Assert.Equal(a.PlayerId, server2.KnownPlayers.First().PlayerId);
            Assert.True(File.Exists(Path.Combine(dir, ServerConfig.FileName)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
