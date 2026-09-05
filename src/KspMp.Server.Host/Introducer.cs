using System.Net;
using KspMp.Shared.Protocol;
using LiteNetLib;

namespace KspMp.Server.Host;

/// <summary>
/// Brokers introductions between servers and players who are both behind home routers.
///
/// Neither side can reach the other to begin with: a router only lets a packet in if something inside asked
/// for it. So both talk to this, which sits on a public address and therefore sees the real external endpoint
/// each router presents. When a player asks for a code this hands each side the other's endpoints, they both
/// send packets outward, and each router - having now seen an outgoing packet to that address - lets the
/// other's replies through.
///
/// It brokers the handshake and nothing else. Once the two are talking, the game traffic is peer to peer and
/// never comes back here, so one small instance serves any number of games.
/// </summary>
internal sealed class Introducer
{
    /// <summary>A server is forgotten this long after its last check-in, so dead hosts do not linger.</summary>
    private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(70);

    private readonly int _port;
    private readonly Action<string> _log;
    private readonly Dictionary<string, Host> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private NatPunchModule _module = null!;

    private readonly record struct Host(IPEndPoint Internal, IPEndPoint External, DateTime SeenAt);

    public Introducer(int port, Action<string> log)
    {
        _port = port;
        _log = log;
    }

    public void Run(CancellationToken stop)
    {
        var listener = new EventBasedNetListener();
        var manager = new NetManager(listener) { NatPunchEnabled = true, UnsyncedEvents = false, IPv6Enabled = false };
        if (!manager.Start(_port)) throw new InvalidOperationException("Could not bind UDP port " + _port);

        _module = manager.NatPunchModule;
        var punch = new EventBasedNatPunchListener();
        punch.NatIntroductionRequest += OnRequest;
        manager.NatPunchModule.Init(punch);

        _log($"KspMp introducer listening on UDP {_port}.");
        _log("Servers register with --introducer <this address> --code <code>; players join with the same code.");

        var nextSweep = DateTime.UtcNow.AddSeconds(30);
        while (!stop.IsCancellationRequested)
        {
            manager.NatPunchModule.PollEvents();
            manager.PollEvents();
            if (DateTime.UtcNow >= nextSweep)
            {
                nextSweep = DateTime.UtcNow.AddSeconds(30);
                Sweep();
            }
            Thread.Sleep(10);
        }
        manager.Stop();
        _log("Introducer stopped.");
    }

    /// <summary>
    /// Requests arrive tagged "H|code" from a server keeping itself registered, or "C|code" from a player
    /// trying to join. A server is remembered; a player is introduced to whichever server holds that code.
    /// </summary>
    private void OnRequest(IPEndPoint local, IPEndPoint remote, string token)
    {
        var split = token?.IndexOf('|') ?? -1;
        if (split <= 0)
        {
            _log($"Ignoring a malformed request from {remote}.");
            return;
        }
        var role = token!.Substring(0, split);
        var code = token.Substring(split + 1);
        if (code.Length == 0) return;

        if (role == "H")
        {
            var known = _hosts.ContainsKey(code);
            _hosts[code] = new Host(local, remote, DateTime.UtcNow);
            if (!known) _log($"Server '{code}' registered from {remote}.");
            return;
        }
        if (role != "C") return;

        if (!_hosts.TryGetValue(code, out var host))
        {
            _log($"{remote} asked for '{code}', which no server is holding.");
            return;
        }
        if (DateTime.UtcNow - host.SeenAt > HostTimeout)
        {
            _hosts.Remove(code);
            _log($"{remote} asked for '{code}', whose server stopped checking in.");
            return;
        }
        _log($"Introducing {remote} to server '{code}' at {host.External}.");
        _module.NatIntroduce(host.Internal, host.External, local, remote, code);
    }

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - HostTimeout;
        foreach (var code in _hosts.Where(h => h.Value.SeenAt < cutoff).Select(h => h.Key).ToList())
        {
            _hosts.Remove(code);
            _log($"Server '{code}' stopped checking in; forgetting it.");
        }
    }
}
