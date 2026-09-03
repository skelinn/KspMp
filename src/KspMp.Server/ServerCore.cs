using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Server
{
    /// <summary>
    /// The multiplayer server. It never runs KSP: it validates, stores and relays. Transport-agnostic so the same
    /// class runs in the console host and, later, in-process inside the game ("Host game").
    /// Single-threaded: call <see cref="Poll"/> from one thread.
    /// </summary>
    public sealed class ServerCore : IDisposable
    {
        private readonly INetTransport _transport;
        private readonly Action<string> _log;
        private readonly NetDataWriter _writer = new NetDataWriter();
        private readonly Dictionary<PeerId, ClientSession> _clients = new Dictionary<PeerId, ClientSession>();
        private readonly List<PendingDisconnect> _pendingDisconnects = new List<PendingDisconnect>();
        private int _nextClientId = 1;

        /// <summary>Delay between sending a Reject and closing the connection, so the reason reaches the client.</summary>
        public int RejectGraceMs = 500;

        private sealed class PendingDisconnect
        {
            public PeerId Peer;
            public string Reason;
            public DateTime DueUtc;
        }

        public ServerCore(INetTransport transport, Action<string> log)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _log = log ?? (_ => { });
            _transport.PeerConnected += OnPeerConnected;
            _transport.PeerDisconnected += OnPeerDisconnected;
            _transport.Received += OnReceived;
        }

        public IEnumerable<ClientSession> Clients => _clients.Values;
        public int ClientCount => _clients.Count;
        public bool IsRunning => _transport.IsRunning;

        public void Start() => _transport.Start();
        public void Poll()
        {
            _transport.Poll();
            if (_pendingDisconnects.Count > 0) ProcessPendingDisconnects();
        }

        private void ProcessPendingDisconnects()
        {
            var now = DateTime.UtcNow;
            for (var i = _pendingDisconnects.Count - 1; i >= 0; i--)
            {
                var pending = _pendingDisconnects[i];
                if (pending.DueUtc > now) continue;
                _pendingDisconnects.RemoveAt(i);
                _transport.Disconnect(pending.Peer, pending.Reason);
            }
        }

        /// <summary>Sends a Reject, then disconnects after <see cref="RejectGraceMs"/> (the reason also rides in the disconnect payload).</summary>
        private void Reject(ClientSession client, string reason)
        {
            client.Rejected = true;
            Send(client.Peer, MessageId.Reject, new RejectMsg { Reason = reason }, Channel.Control, Delivery.ReliableOrdered);
            _pendingDisconnects.Add(new PendingDisconnect { Peer = client.Peer, Reason = reason, DueUtc = DateTime.UtcNow.AddMilliseconds(RejectGraceMs) });
        }
        public void Stop() => _transport.Stop();
        public void Dispose() => Stop();

        private void OnPeerConnected(PeerId peer)
        {
            _clients[peer] = new ClientSession { Peer = peer };
            _log(peer + " connected, awaiting hello");
        }

        private void OnPeerDisconnected(PeerId peer, string reason)
        {
            if (!_clients.TryGetValue(peer, out var client)) return;
            _clients.Remove(peer);
            _log(client.DisplayName + " disconnected (" + reason + ")");
        }

        private void OnReceived(PeerId from, byte[] buffer, int offset, int length, Channel channel)
        {
            if (!_clients.TryGetValue(from, out var client) || client.Rejected) return;
            var reader = new NetDataReader(buffer, offset, length);
            if (!Envelope.TryReadHeader(reader, out var id, out var flags, out _)) return;
            try
            {
                var body = Envelope.OpenBody(reader, flags);
                Handle(client, id, body);
            }
            catch (Exception e)
            {
                _log("Error handling " + id + " from " + client.DisplayName + ": " + e);
            }
        }

        private void Handle(ClientSession client, MessageId id, NetDataReader body)
        {
            switch (id)
            {
                case MessageId.Hello:
                    HandleHello(client, Envelope.Read<HelloMsg>(body));
                    break;

                case MessageId.Ping:
                {
                    var ping = Envelope.Read<PingMsg>(body);
                    Send(client.Peer, MessageId.Pong, new PongMsg { ClientTicks = ping.ClientTicks, ServerTicks = DateTime.UtcNow.Ticks }, Channel.State, Delivery.Unreliable);
                    break;
                }

                default:
                    _log("Unhandled message " + id + " from " + client.DisplayName);
                    break;
            }
        }

        private void HandleHello(ClientSession client, HelloMsg hello)
        {
            if (hello.ProtocolVersion != ProtocolVersion.Current)
            {
                Reject(client, "Protocol version mismatch: server " + ProtocolVersion.Current + ", client " + hello.ProtocolVersion);
                return;
            }

            client.ClientId = _nextClientId++;
            client.PlayerId = hello.PlayerId;
            client.PlayerName = string.IsNullOrEmpty(hello.PlayerName) ? "Player" : hello.PlayerName;
            client.Handshaken = true;
            _log(client.DisplayName + " joined (KSP " + hello.KspVersion + ", mod " + hello.ModVersion + ")");

            Send(client.Peer, MessageId.Welcome, new WelcomeMsg { ClientId = client.ClientId, UniversalTime = 0, NeedsAvatar = false }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void Send<T>(PeerId to, MessageId id, T message, Channel channel, Delivery delivery) where T : INetSerializable
        {
            Envelope.Write(_writer, id, message);
            _transport.Send(to, _writer.Data, 0, _writer.Length, channel, delivery);
        }
    }
}
