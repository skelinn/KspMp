using KspMp.Shared.Protocol;
using Xunit;

namespace KspMp.Shared.Tests;

public class LoopbackTransportTests
{
    [Fact]
    public void DeliversInOrderAndReportsConnectionEvents()
    {
        var hub = new LoopbackHub();
        var server = hub.CreateServer();
        var client = hub.CreateClient();
        var serverLog = new List<string>();
        var clientLog = new List<string>();
        server.PeerConnected += p => serverLog.Add("connect " + p);
        server.PeerDisconnected += (p, r) => serverLog.Add("disconnect " + p + " " + r);
        server.Received += (p, buf, off, len, ch) => { serverLog.Add("data " + buf[off] + " ch" + (int)ch); server.Send(p, new byte[] { (byte)(buf[off] + 100) }, 0, 1, ch, Delivery.Unreliable); };
        client.PeerConnected += p => clientLog.Add("connect " + p);
        client.PeerDisconnected += (p, r) => clientLog.Add("disconnect " + p + " " + r);
        client.Received += (p, buf, off, len, ch) => clientLog.Add("data " + buf[off]);

        server.Start();
        client.Start();
        client.Send(PeerId.Server, new byte[] { 1 }, 0, 1, Channel.Control, Delivery.ReliableOrdered);
        client.Send(PeerId.Server, new byte[] { 2 }, 0, 1, Channel.State, Delivery.Sequenced);
        server.Poll();
        client.Poll();
        client.Stop();
        server.Poll();
        client.Poll();

        Assert.Equal(new[] { "connect peer#0", "data 1 ch0", "data 2 ch1", "disconnect peer#0 closed" }, serverLog);
        Assert.Equal(new[] { "connect server", "data 101", "data 102", "disconnect server closed" }, clientLog);
        Assert.Equal(0, server.PeerCount);
    }

    [Fact]
    public void ServerCanDisconnectAClient()
    {
        var hub = new LoopbackHub();
        var server = hub.CreateServer();
        var client = hub.CreateClient();
        string? clientReason = null;
        string? serverReason = null;
        client.PeerDisconnected += (_, r) => clientReason = r;
        server.PeerDisconnected += (_, r) => serverReason = r;
        server.Start();
        client.Start();
        server.Poll();
        server.Disconnect(new PeerId(0), "kicked");
        server.Poll();
        client.Poll();
        Assert.Equal("kicked", clientReason);
        Assert.Equal("kicked", serverReason);
        Assert.False(client.IsRunning);
    }
}
