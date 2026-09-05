using System;
using System.Collections.Generic;

namespace KspMp.Shared.Protocol
{
    /// <summary>
    /// Runs several transports as one, so a single server can be reached more than one way at a time.
    ///
    /// A host needs this because the ways in are not interchangeable. Players elsewhere arrive over Steam,
    /// which asks nothing of anybody's router; players on the same network, and the host's own game, arrive
    /// over the UDP socket, because a host cannot send Steam packets to itself. One server, one world, two
    /// front doors.
    ///
    /// Peer numbers from the children would collide, so each child is given a band of the peer space and its
    /// peers are shifted into it. Nothing outside here sees the difference.
    /// </summary>
    public sealed class CompositeTransport : INetTransport
    {
        /// <summary>Peers each child may have before its band would run into the next one.</summary>
        public const int PeersPerTransport = 10000;

        private readonly INetTransport[] _children;
        private readonly Action<string> _log;

        public CompositeTransport(IEnumerable<INetTransport> children, Action<string> log = null)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            _children = new List<INetTransport>(children).ToArray();
            if (_children.Length == 0) throw new ArgumentException("A composite needs at least one transport.", nameof(children));
            _log = log ?? (_ => { });

            for (var i = 0; i < _children.Length; i++)
            {
                var index = i;
                var child = _children[i];
                child.PeerConnected += peer => PeerConnected?.Invoke(ToOuter(index, peer));
                child.PeerDisconnected += (peer, reason) => PeerDisconnected?.Invoke(ToOuter(index, peer), reason);
                child.Received += (from, buffer, offset, length, channel) =>
                    Received?.Invoke(ToOuter(index, from), buffer, offset, length, channel);
            }
        }

        public bool IsRunning
        {
            get
            {
                foreach (var child in _children) if (child.IsRunning) return true;
                return false;
            }
        }

        public event Action<PeerId> PeerConnected;
        public event Action<PeerId, string> PeerDisconnected;
        public event ReceivedHandler Received;

        /// <summary>
        /// One child failing to start is not fatal: a host with no Steam should still be reachable over UDP,
        /// and a host whose port is taken can still be reached over Steam. Only every route failing is.
        /// </summary>
        public void Start()
        {
            var started = 0;
            for (var i = 0; i < _children.Length; i++)
            {
                try
                {
                    _children[i].Start();
                    started++;
                }
                catch (Exception e)
                {
                    _log("Transport " + _children[i].GetType().Name + " could not start: " + e.Message);
                }
            }
            if (started == 0) throw new InvalidOperationException("No transport could start; the server is unreachable.");
        }

        public void Stop()
        {
            foreach (var child in _children)
            {
                try { child.Stop(); }
                catch (Exception e) { _log("Stopping " + child.GetType().Name + ": " + e.Message); }
            }
        }

        public void Poll()
        {
            foreach (var child in _children)
            {
                try { child.Poll(); }
                catch (Exception e) { _log("Polling " + child.GetType().Name + ": " + e.Message); }
            }
        }

        public void Send(PeerId to, byte[] data, int offset, int length, Channel channel, Delivery delivery)
        {
            var child = ChildOf(to, out var inner);
            child?.Send(inner, data, offset, length, channel, delivery);
        }

        public void Disconnect(PeerId peer, string reason)
        {
            var child = ChildOf(peer, out var inner);
            child?.Disconnect(inner, reason);
        }

        public int GetPeerPingMs(PeerId peer)
        {
            var child = ChildOf(peer, out var inner);
            return child != null ? child.GetPeerPingMs(inner) : 0;
        }

        /// <summary>Shifts a child's peer into that child's band. The server peer is never rewritten.</summary>
        private static PeerId ToOuter(int index, PeerId peer)
        {
            if (peer.IsServer || peer.IsNone) return peer;
            return new PeerId(index * PeersPerTransport + peer.Value);
        }

        private INetTransport ChildOf(PeerId peer, out PeerId inner)
        {
            if (peer.IsServer || peer.IsNone)
            {
                // A composite only ever runs as a server, so nothing here addresses a server peer.
                inner = peer;
                return null;
            }
            var index = peer.Value / PeersPerTransport;
            if (index < 0 || index >= _children.Length)
            {
                inner = PeerId.None;
                return null;
            }
            inner = new PeerId(peer.Value % PeersPerTransport);
            return _children[index];
        }

        public void Dispose()
        {
            foreach (var child in _children)
            {
                try { child.Dispose(); }
                catch (Exception e) { _log("Disposing " + child.GetType().Name + ": " + e.Message); }
            }
        }
    }
}
