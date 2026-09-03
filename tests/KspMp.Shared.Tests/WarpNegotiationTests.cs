using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class WarpNegotiationTests
{
    private static ServerCore NewServer(LoopbackHub hub, ServerConfig? config = null)
    {
        var server = new ServerCore(hub.CreateServer(), config ?? new ServerConfig(), new UniverseStore(null), _ => { });
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

    private static void Want(TestClient client, int index, WarpMode mode = WarpMode.Rails, int maxRails = -1) =>
        client.Send(MessageId.WarpRequest, new WarpRequestMsg { Mode = mode, DesiredIndex = index, MaxRailsIndex = maxRails });

    [Fact]
    public void SlowestWishWinsAndAnyoneCanCancel()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);
        Assert.Equal(1f, a.Last<WarpStateMsg>()!.Value.Rate);

        Want(a, 5); // 1000x
        TestClient.Pump(server, a, b);
        var state = b.Last<WarpStateMsg>()!.Value;
        Assert.Equal(1000f, state.Rate);
        Assert.Equal(a.ClientId, state.RequesterClientId);
        Assert.Equal(1000f, server.Time.Rate);

        Want(b, 2); // 10x is slower, so it wins
        TestClient.Pump(server, a, b);
        state = a.Last<WarpStateMsg>()!.Value;
        Assert.Equal(10f, state.Rate);
        Assert.Equal(b.ClientId, state.RequesterClientId);

        Want(b, 0); // Bob no longer cares: Alice's 1000x stands
        TestClient.Pump(server, a, b);
        Assert.Equal(1000f, a.Last<WarpStateMsg>()!.Value.Rate);

        Want(a, 0); // nobody warps
        TestClient.Pump(server, a, b);
        Assert.Equal(1f, b.Last<WarpStateMsg>()!.Value.Rate);
        Assert.Equal(1f, server.Time.Rate);
    }

    [Fact]
    public void AltitudeLimitOfAnyClientCapsRailsWarp()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub);
        var a = Join(hub, server, "Alice");
        var b = Join(hub, server, "Bob", a);

        Want(b, 0, WarpMode.Rails, maxRails: 1); // Bob is low in the atmosphere: at most 5x
        Want(a, 5);
        TestClient.Pump(server, a, b);
        var state = a.Last<WarpStateMsg>()!.Value;
        Assert.Equal(5f, state.Rate);
        Assert.Equal(a.ClientId, state.RequesterClientId);
        Assert.Equal(b.ClientId, state.LimitingClientId);

        b.Stop(); // Bob leaves: the cap goes with him
        TestClient.Pump(server, a, b);
        Assert.Equal(1000f, a.Last<WarpStateMsg>()!.Value.Rate);
    }

    [Fact]
    public void PhysicsWarpIsComparedByRateAndHostModeIgnoresOthers()
    {
        var hub = new LoopbackHub();
        using var server = NewServer(hub, new ServerConfig { HostControlsWarp = true });
        var host = Join(hub, server, "Host");
        var guest = Join(hub, server, "Guest", host);

        Want(guest, 3, WarpMode.Physics); // 4x physics, but guests do not decide
        TestClient.Pump(server, host, guest);
        Assert.Equal(1f, server.Warp.Rate);

        Want(host, 3, WarpMode.Physics);
        TestClient.Pump(server, host, guest);
        var state = guest.Last<WarpStateMsg>()!.Value;
        Assert.Equal(4f, state.Rate);
        Assert.Equal(WarpMode.Physics, state.Mode);
        Assert.Equal(4f, server.Time.Rate);
    }
}
