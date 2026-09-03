using System;
using System.Collections.Generic;

namespace KspMp.Shared.Protocol
{
    /// <summary>
    /// Creates paired in-memory transports: one server and any number of clients. Used by the tests and, later,
    /// by the in-game "Host game" mode where the server runs inside the KSP process.
    /// </summary>
    public sealed class LoopbackHub
    {
        private int _nextPeerId;

        internal LoopbackTransport Server { get; private set; }

        public LoopbackTransport CreateServer()
        {
            if (Server != null) throw new InvalidOperationException("Hub already has a server");
            Server = new LoopbackTransport(this, true);
            return Server;
        }

        public LoopbackTransport CreateClient() => new LoopbackTransport(this, false);

        internal int NextPeerId() => _nextPeerId++;
    }

    /// <summary>In-memory transport. Every delivery mode behaves as reliable-ordered; events fire from <see cref="Poll"/>.</summary>
    public sealed class LoopbackTransport : INetTransport
    {
        private enum Kind { Connected, Disconnected, Data }

        private struct Event
        {
            public Kind Kind;
            public PeerId From;
            public byte[] Data;
            public Channel Channel;
            public string Reason;
        }

        private readonly LoopbackHub _hub;
        private readonly bool _isServer;
        private readonly Queue<Event> _inbox = new Queue<Event>();
        private readonly Dictionary<int, LoopbackTransport> _clients = new Dictionary<int, LoopbackTransport>();
        private LoopbackTransport _server;
        private int _peerId = -1;

        internal LoopbackTransport(LoopbackHub hub, bool isServer)
        {
            _hub = hub;
            _isServer = isServer;
        }

        public bool IsServer => _isServer;
        public bool IsRunning { get; private set; }
        public int PeerCount => _clients.Count;

        public event Action<PeerId> PeerConnected;
        public event Action<PeerId, string> PeerDisconnected;
        public event ReceivedHandler Received;

        private PeerId SelfAsSeenByRemote => _isServer ? PeerId.Server : new PeerId(_peerId);

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            if (_isServer) return;

            _server = _hub.Server;
            if (_server == null || !_server.IsRunning) throw new InvalidOperationException("Loopback server is not running");
            _peerId = _hub.NextPeerId();
            _server._clients[_peerId] = this;
            _server._inbox.Enqueue(new Event { Kind = Kind.Connected, From = new PeerId(_peerId) });
            _inbox.Enqueue(new Event { Kind = Kind.Connected, From = PeerId.Server });
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            if (_isServer)
            {
                foreach (var client in _clients.Values)
                {
                    client._server = null;
                    client._inbox.Enqueue(new Event { Kind = Kind.Disconnected, From = PeerId.Server, Reason = "server stopped" });
                }
                _clients.Clear();
            }
            else if (_server != null)
            {
                if (_server._clients.Remove(_peerId))
                    _server._inbox.Enqueue(new Event { Kind = Kind.Disconnected, From = new PeerId(_peerId), Reason = "closed" });
                _server = null;
                _inbox.Enqueue(new Event { Kind = Kind.Disconnected, From = PeerId.Server, Reason = "closed" });
            }
        }

        public void Poll()
        {
            while (_inbox.Count > 0)
            {
                var e = _inbox.Dequeue();
                switch (e.Kind)
                {
                    case Kind.Connected:
                        PeerConnected?.Invoke(e.From);
                        break;
                    case Kind.Disconnected:
                        PeerDisconnected?.Invoke(e.From, e.Reason);
                        break;
                    case Kind.Data:
                        Received?.Invoke(e.From, e.Data, 0, e.Data.Length, e.Channel);
                        break;
                }
            }
        }

        public void Send(PeerId to, byte[] data, int offset, int length, Channel channel, Delivery delivery)
        {
            var target = Resolve(to);
            if (target == null || !IsRunning) return;
            var copy = new byte[length];
            Buffer.BlockCopy(data, offset, copy, 0, length);
            target._inbox.Enqueue(new Event { Kind = Kind.Data, From = SelfAsSeenByRemote, Data = copy, Channel = channel });
        }

        public void Disconnect(PeerId peer, string reason)
        {
            if (_isServer)
            {
                if (!_clients.TryGetValue(peer.Value, out var client)) return;
                _clients.Remove(peer.Value);
                client._server = null;
                client.IsRunning = false;
                client._inbox.Enqueue(new Event { Kind = Kind.Disconnected, From = PeerId.Server, Reason = reason });
                _inbox.Enqueue(new Event { Kind = Kind.Disconnected, From = peer, Reason = reason });
            }
            else
            {
                Stop();
            }
        }

        public int GetPeerPingMs(PeerId peer) => 0;

        public void Dispose() => Stop();

        private LoopbackTransport Resolve(PeerId id)
        {
            if (_isServer)
            {
                _clients.TryGetValue(id.Value, out var client);
                return client;
            }
            return id.IsServer ? _server : null;
        }
    }
}
