using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Net
{
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Handshaking,
        Connected,
    }

    /// <summary>
    /// The client's connection to the server: transport, handshake, and dispatch of incoming messages to handlers
    /// registered by the systems. Everything runs on the Unity main thread from <see cref="Poll"/>.
    /// </summary>
    public sealed class ClientNetwork
    {
        private readonly Settings _settings;
        private readonly NetDataWriter _writer = new NetDataWriter();
        private readonly Dictionary<MessageId, Action<NetDataReader>> _handlers = new Dictionary<MessageId, Action<NetDataReader>>();
        private readonly HashSet<MessageId> _warnedUnhandled = new HashSet<MessageId>();
        private LiteNetLibTransport _transport;
        private bool _stopRequested;
        private string _stopReason;

        public ClientNetwork(Settings settings)
        {
            _settings = settings;
            RegisterHandler(MessageId.Welcome, OnWelcome);
            RegisterHandler(MessageId.Reject, OnReject);
        }

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public string Status { get; private set; } = "Not connected";
        public string LastError { get; private set; }
        public string ServerAddress { get; private set; }
        public int ServerPort { get; private set; }
        public int ClientId { get; private set; }
        public string ServerName { get; private set; }
        public WelcomeMsg Welcome { get; private set; }
        public bool IsConnected => State == ConnectionState.Connected;
        public int PingMs => _transport != null ? _transport.PingMs : 0;

        /// <summary>Raised once the server accepted us (after Welcome).</summary>
        public event Action<WelcomeMsg> Welcomed;
        /// <summary>Raised whenever a connection attempt or an established connection ends.</summary>
        public event Action<string> Disconnected;

        public void RegisterHandler(MessageId id, Action<NetDataReader> handler) => _handlers[id] = handler;

        public void UnregisterHandler(MessageId id, Action<NetDataReader> handler)
        {
            if (_handlers.TryGetValue(id, out var current) && current == handler) _handlers.Remove(id);
        }

        /// <summary>Server password for the next connection; empty for servers that do not ask for one.</summary>
        public string Password { get; set; } = string.Empty;

        public void Connect(string address, int port) => Connect(address, port, null, null);

        /// <summary>
        /// Connects, optionally by join code. With an introducer and a code we ask to be introduced to the
        /// server rather than dialling it, which is what lets two people behind home routers reach each other
        /// without either forwarding a port. The address stays as the fallback if nobody answers.
        /// </summary>
        public void Connect(string address, int port, string introducer, string joinCode)
        {
            if (_transport != null) Disconnect("reconnecting");
            try
            {
                _transport = new LiteNetLibTransport(new TransportOptions
                {
                    IsServer = false,
                    Address = address,
                    Port = port,
                    Introducer = introducer ?? string.Empty,
                    JoinCode = joinCode ?? string.Empty,
                }, m => Log.Info("[net] " + m));
                _transport.PeerConnected += OnPeerConnected;
                _transport.PeerDisconnected += OnPeerDisconnected;
                _transport.Received += OnReceived;
                _transport.Start();
                ServerAddress = address;
                ServerPort = port;
                LastError = null;
                State = ConnectionState.Connecting;
                Status = !string.IsNullOrEmpty(joinCode)
                    ? "Looking for '" + joinCode + "' via " + introducer + " ..."
                    : "Connecting to " + address + ":" + port + " ...";
            }
            catch (Exception e)
            {
                Log.Exception("Connect", e);
                _transport = null;
                State = ConnectionState.Disconnected;
                LastError = e.Message;
                Status = "Connect failed: " + e.Message;
            }
        }

        public void Disconnect(string reason)
        {
            if (_transport == null) return;
            var transport = _transport;
            _transport = null;
            _stopRequested = false;
            try
            {
                transport.Stop();
            }
            catch (Exception e)
            {
                Log.Exception("Disconnect", e);
            }
            FinishDisconnect(reason);
        }

        public void Poll()
        {
            if (_stopRequested)
            {
                _stopRequested = false;
                Disconnect(_stopReason);
                return;
            }
            _transport?.Poll();
        }

        public void Send<T>(MessageId id, T message, Channel channel, Delivery delivery) where T : INetSerializable
        {
            if (_transport == null) return;
            Envelope.Write(_writer, id, message);
            _transport.Send(PeerId.Server, _writer.Data, 0, _writer.Length, channel, delivery);
        }

        private void FinishDisconnect(string reason)
        {
            var wasActive = State != ConnectionState.Disconnected;
            State = ConnectionState.Disconnected;
            Status = "Disconnected: " + reason;
            ClientId = 0;
            if (wasActive)
            {
                Log.Info("Disconnected: " + reason);
                Disconnected?.Invoke(reason);
            }
        }

        // ---- transport events (inside Poll) ----

        private void OnPeerConnected(PeerId peer)
        {
            State = ConnectionState.Handshaking;
            Status = "Connected, waiting for the server ...";
            Send(MessageId.Hello, new HelloMsg
            {
                ProtocolVersion = ProtocolVersion.Current,
                ModVersion = KspMpAddon.Version,
                PlayerId = _settings.PlayerId,
                PlayerName = _settings.PlayerName,
                KspVersion = Versioning.GetVersionString(),
                PasswordHash = PasswordHash.Of(Password),
            }, Channel.Control, Delivery.ReliableOrdered);
        }

        private void OnPeerDisconnected(PeerId peer, string reason)
        {
            // Stop the transport outside its own callback.
            _stopRequested = true;
            _stopReason = LastError ?? reason;
        }

        private void OnReceived(PeerId from, byte[] buffer, int offset, int length, Channel channel)
        {
            var reader = new NetDataReader(buffer, offset, length);
            if (!Envelope.TryReadHeader(reader, out var id, out var flags, out _)) return;
            if (!_handlers.TryGetValue(id, out var handler))
            {
                if (_warnedUnhandled.Add(id)) Log.Warn("No handler for message " + id);
                return;
            }
            try
            {
                handler(Envelope.OpenBody(reader, flags));
            }
            catch (Exception e)
            {
                Log.Exception("Handling " + id, e);
            }
        }

        private void OnWelcome(NetDataReader body)
        {
            var welcome = Envelope.Read<WelcomeMsg>(body);
            Welcome = welcome;
            ClientId = welcome.ClientId;
            ServerName = welcome.ServerName;
            State = ConnectionState.Connected;
            Status = "Connected to " + ServerName + " as #" + ClientId;
            Log.Info(Status + " (server UT " + welcome.UniversalTime.ToString("F1") + ", rate " + welcome.TimeRate + "x)");
            Welcomed?.Invoke(welcome);
        }

        private void OnReject(NetDataReader body)
        {
            var reject = Envelope.Read<RejectMsg>(body);
            LastError = reject.Reason;
            Status = "Rejected: " + reject.Reason;
            Log.Warn("Server rejected the connection: " + reject.Reason);
        }
    }
}
