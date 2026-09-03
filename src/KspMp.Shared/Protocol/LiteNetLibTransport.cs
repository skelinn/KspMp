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
            };

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
                _serverPeer = _manager.Connect(_options.Address, _options.Port, _options.ConnectionKey);
                _log("Connecting to " + _options.Address + ":" + _options.Port);
            }
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
