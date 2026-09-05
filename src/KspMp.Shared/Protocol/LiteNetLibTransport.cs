using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    /// <summary>UDP transport over LiteNetLib. One instance is either a server or a client.</summary>
    public sealed class LiteNetLibTransport : INetTransport, INetEventListener
    {
        private readonly TransportOptions _options;
        private readonly Action<string> _log;
        private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();
        private NetManager _manager;
        private NetPeer _serverPeer;

        public LiteNetLibTransport(TransportOptions options, Action<string> log = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _log = log ?? (_ => { });
        }

        public bool IsServer => _options.IsServer;
        public bool IsRunning => _manager != null && _manager.IsRunning;
        public int LocalPort => _manager != null ? _manager.LocalPort : 0;
        public int PeerCount => _peers.Count;
        public int PingMs => _serverPeer != null ? _serverPeer.Ping : 0;

        public event Action<PeerId> PeerConnected;
        public event Action<PeerId, string> PeerDisconnected;
        public event ReceivedHandler Received;

        public void Start()
        {
            if (_manager != null) throw new InvalidOperationException("Transport already started");
            _manager = new NetManager(this)
            {
                UnsyncedEvents = false,     // all callbacks fire inside PollEvents() on the polling thread
                ChannelsCount = 4,
                UpdateTime = _options.UpdateTimeMs,
                DisconnectTimeout = _options.DisconnectTimeoutMs,
                IPv6Enabled = false,
                AutoRecycle = true,
                UseNativeSockets = false,
                NatPunchEnabled = _options.UsesIntroducer,
            };
            if (_options.UsesIntroducer) StartPunching();

            if (_options.IsServer)
            {
                if (!_manager.Start(_options.Port))
                    throw new InvalidOperationException("Could not bind UDP port " + _options.Port);
                _log("Listening on UDP port " + _manager.LocalPort);
            }
            else
            {
                if (!_manager.Start())
                    throw new InvalidOperationException("Could not open a UDP socket");
                if (_options.UsesIntroducer)
                {
                    // Ask to be introduced rather than dialling an address we cannot reach. If nobody answers
                    // in time we fall back to a direct connection, which still works on a LAN or a VPN.
                    _punchDeadline = DateTime.UtcNow.AddMilliseconds(_options.PunchTimeoutMs);
                    RequestIntroduction("C");
                    _log("Asking " + _options.Introducer + " to introduce us to '" + _options.JoinCode + "'");
                }
                else
                {
                    _serverPeer = _manager.Connect(_options.Address, _options.Port, _options.ConnectionKey);
                    _log("Connecting to " + _options.Address + ":" + _options.Port);
                }
            }
        }

        // ---- NAT hole punching ----

        private const double RegisterIntervalSeconds = 20;
        private DateTime _nextRegisterAt;
        private DateTime _punchDeadline;

        /// <summary>
        /// Both sides ask the introducer to pair them by join code. It replies to each with the other's
        /// address, and the packets they then send each other punch a hole through their own routers.
        /// </summary>
        private void StartPunching()
        {
            var listener = new EventBasedNatPunchListener();
            listener.NatIntroductionSuccess += OnIntroduced;
            _manager.NatPunchModule.Init(listener);
            _nextRegisterAt = DateTime.UtcNow;
        }

        private void RequestIntroduction(string role)
        {
            var introducer = ParseEndPoint(_options.Introducer);
            if (introducer == null)
            {
                _log("Introducer address '" + _options.Introducer + "' is not host:port; skipping hole punching.");
                return;
            }
            try { _manager.NatPunchModule.SendNatIntroduceRequest(introducer, role + "|" + _options.JoinCode); }
            catch (Exception e) { _log("Could not reach the introducer: " + e.Message); }
        }

        private void OnIntroduced(IPEndPoint target, NatAddressType type, string token)
        {
            if (_options.IsServer)
            {
                // Nothing to dial: the introduce packet we just sent has already opened our router for this
                // peer, so its connection request will get through.
                _log("Introduced to a player at " + target + " (" + type + "); waiting for them to connect.");
                return;
            }
            if (_serverPeer != null) return;
            _punchDeadline = default;
            _log("Introduced to the server at " + target + " (" + type + "); connecting.");
            _serverPeer = _manager.Connect(target, _options.ConnectionKey);
        }

        /// <summary>Parses "host:port", resolving a name if it is not already an address.</summary>
        internal static IPEndPoint ParseEndPoint(string hostPort)
        {
            if (string.IsNullOrEmpty(hostPort)) return null;
            var colon = hostPort.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(hostPort.Substring(colon + 1), out var port)) return null;
            var host = hostPort.Substring(0, colon);
            if (IPAddress.TryParse(host, out var address)) return new IPEndPoint(address, port);
            try
            {
                foreach (var resolved in Dns.GetHostAddresses(host))
                    if (resolved.AddressFamily == AddressFamily.InterNetwork) return new IPEndPoint(resolved, port);
            }
            catch { /* unresolvable: treated as no introducer */ }
            return null;
        }

        public void Stop()
        {
            if (_manager == null) return;
            _manager.Stop();
            _manager = null;
            _serverPeer = null;
            _peers.Clear();
        }

        public void Poll()
        {
            _manager?.PollEvents();
            if (_manager == null || !_options.UsesIntroducer) return;
            _manager.NatPunchModule.PollEvents();

            var now = DateTime.UtcNow;
            if (_options.IsServer)
            {
                // Keep our endpoints fresh at the introducer: a NAT mapping expires, and the address it saw
                // last week is no use to anyone trying to join today.
                if (now >= _nextRegisterAt)
                {
                    _nextRegisterAt = now.AddSeconds(RegisterIntervalSeconds);
                    RequestIntroduction("H");
                }
            }
            else if (_serverPeer == null && _punchDeadline != default && now >= _punchDeadline)
            {
                _punchDeadline = default;
                _log("Nobody introduced us within " + (_options.PunchTimeoutMs / 1000) + "s; trying "
                     + _options.Address + ":" + _options.Port + " directly instead.");
                _serverPeer = _manager.Connect(_options.Address, _options.Port, _options.ConnectionKey);
            }
        }

        public void Send(PeerId to, byte[] data, int offset, int length, Channel channel, Delivery delivery)
        {
            var peer = ResolvePeer(to);
            if (peer == null) return;
            peer.Send(data, offset, length, (byte)channel, ToDeliveryMethod(delivery));
        }

        public void Disconnect(PeerId peerId, string reason)
        {
            var peer = ResolvePeer(peerId);
            if (peer == null || _manager == null) return;
            var writer = new NetDataWriter();
            writer.Put(reason ?? string.Empty);
            _manager.DisconnectPeer(peer, writer);
        }

        public int GetPeerPingMs(PeerId peerId)
        {
            var peer = ResolvePeer(peerId);
            return peer != null ? peer.Ping : 0;
        }

        public void Dispose()
        {
            Stop();
        }

        private NetPeer ResolvePeer(PeerId id)
        {
            if (_options.IsServer)
            {
                _peers.TryGetValue(id.Value, out var peer);
                return peer;
            }
            return id.IsServer ? _serverPeer : null;
        }

        private PeerId ToPeerId(NetPeer peer) => _options.IsServer ? new PeerId(peer.Id) : PeerId.Server;

        private static DeliveryMethod ToDeliveryMethod(Delivery delivery)
        {
            switch (delivery)
            {
                case Delivery.ReliableOrdered: return DeliveryMethod.ReliableOrdered;
                case Delivery.ReliableUnordered: return DeliveryMethod.ReliableUnordered;
                case Delivery.Sequenced: return DeliveryMethod.Sequenced;
                default: return DeliveryMethod.Unreliable;
            }
        }

        // ---- INetEventListener (invoked from PollEvents) ----

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            if (_options.IsServer) _peers[peer.Id] = peer;
            _log("Peer connected: " + ToPeerId(peer));
            PeerConnected?.Invoke(ToPeerId(peer));
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (_options.IsServer) _peers.Remove(peer.Id);
            if (ReferenceEquals(peer, _serverPeer)) _serverPeer = null;
            var reason = info.Reason.ToString();
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
            {
                try { reason += ": " + info.AdditionalData.GetString(); } catch { /* not a string payload */ }
            }
            _log("Peer disconnected: " + ToPeerId(peer) + " (" + reason + ")");
            PeerDisconnected?.Invoke(ToPeerId(peer), reason);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            _log("Socket error " + socketError + " (" + endPoint + ")");
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            Received?.Invoke(ToPeerId(peer), reader.RawData, reader.UserDataOffset, reader.UserDataSize, (Channel)channelNumber);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            if (!_options.IsServer || _peers.Count >= _options.MaxPeers)
            {
                request.Reject();
                return;
            }
            request.AcceptIfKey(_options.ConnectionKey);
        }
    }
}
