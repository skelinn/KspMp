using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;

namespace KspMp.Net.Steam
{
    /// <summary>
    /// Carries the game protocol over Steam's peer-to-peer packets instead of a UDP socket, so players can
    /// reach each other without forwarding a port. Steam tries a direct link first and falls back to its own
    /// relay when it cannot get one, which is what reaches people behind carrier-grade NAT.
    ///
    /// Steam P2P has no notion of connecting, only of packets arriving, so this synthesises the connect and
    /// disconnect events the rest of the mod expects: a joining player repeats a one-byte probe until the host
    /// answers, and silence for long enough counts as a disconnect.
    ///
    /// One limitation to be aware of. Steam drops packets from anyone you have not accepted a session with,
    /// and you normally learn that someone wants in from a P2PSessionRequest callback. Registering a Steam
    /// callback from C# means hand-building a vtable, so instead a host accepts the Steam IDs it has been told
    /// to expect. That is fine for playing with friends, whose IDs you have; hosting for strangers is what
    /// would justify writing the callback machinery.
    /// </summary>
    public sealed class SteamP2PTransport : INetTransport
    {
        private const byte Probe = 0xFE;
        private const byte ProbeAck = 0xFD;
        private const int Channels = 4;
        private const double ProbeIntervalSeconds = 0.5;
        private const double ConnectTimeoutSeconds = 20;
        private const double PeerSilenceSeconds = 25;

        private readonly bool _isServer;
        private readonly ulong _hostSteamId;
        private readonly HashSet<ulong> _expected = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> _peerBySteam = new Dictionary<ulong, int>();
        private readonly Dictionary<int, ulong> _steamByPeer = new Dictionary<int, ulong>();
        private readonly Dictionary<ulong, DateTime> _lastHeard = new Dictionary<ulong, DateTime>();
        private readonly Action<string> _log;
        private byte[] _buffer = new byte[64 * 1024];
        private int _nextPeer;
        private DateTime _nextProbeAt;
        private DateTime _giveUpAt;
        private bool _connected;

        public SteamP2PTransport(bool isServer, ulong hostSteamId, IEnumerable<ulong> expectedPlayers, Action<string> log = null)
        {
            _isServer = isServer;
            _hostSteamId = hostSteamId;
            _log = log ?? (_ => { });
            if (expectedPlayers != null)
                foreach (var id in expectedPlayers)
                    if (id != 0) _expected.Add(id);
        }

        public bool IsRunning { get; private set; }

        public event Action<PeerId> PeerConnected;
        public event Action<PeerId, string> PeerDisconnected;
        public event ReceivedHandler Received;

        public void Start()
        {
            if (!SteamP2P.TryInitialise())
                throw new InvalidOperationException("Steam is not available: " + SteamP2P.Unavailable);

            IsRunning = true;
            if (_isServer)
            {
                // Accept up front, because a session has to be open before Steam will deliver anything.
                foreach (var id in _expected)
                    SteamNative.SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(SteamP2P.Networking, id);
                _log("Hosting over Steam as " + SteamP2P.LocalSteamId + "; expecting " + _expected.Count + " player(s).");
            }
            else
            {
                if (_hostSteamId == 0) throw new InvalidOperationException("No host Steam ID to join.");
                SteamNative.SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(SteamP2P.Networking, _hostSteamId);
                _nextProbeAt = DateTime.UtcNow;
                _giveUpAt = DateTime.UtcNow.AddSeconds(ConnectTimeoutSeconds);
                _log("Joining " + _hostSteamId + " over Steam.");
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            foreach (var id in new List<ulong>(_steamByPeer.Values))
                SteamNative.SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(SteamP2P.Networking, id);
            if (!_isServer && _hostSteamId != 0)
                SteamNative.SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(SteamP2P.Networking, _hostSteamId);
            _peerBySteam.Clear();
            _steamByPeer.Clear();
            _lastHeard.Clear();
            _connected = false;
        }

        public void Poll()
        {
            if (!IsRunning) return;
            SteamP2P.Poll();

            for (var channel = 0; channel < Channels; channel++)
                while (SteamNative.SteamAPI_ISteamNetworking_IsP2PPacketAvailable(SteamP2P.Networking, out var size, channel))
                {
                    if (size > _buffer.Length) _buffer = new byte[size];
                    if (!SteamNative.SteamAPI_ISteamNetworking_ReadP2PPacket(
                            SteamP2P.Networking, _buffer, (uint)_buffer.Length, out var read, out var from, channel))
                        break;
                    Handle(from, (int)read, (Channel)channel);
                }

            if (!_isServer) KeepJoining();
            DropSilentPeers();
        }

        private void Handle(ulong from, int length, Channel channel)
        {
            _lastHeard[from] = DateTime.UtcNow;

            if (length == 1 && _buffer[0] == Probe)
            {
                // Somebody is knocking. Answer so they know we are here, and count them in.
                SendTo(from, new[] { ProbeAck }, 0, 1, Channel.Control, Delivery.ReliableOrdered);
                if (_isServer) EnsurePeer(from);
                return;
            }
            if (length == 1 && _buffer[0] == ProbeAck)
            {
                if (!_isServer && !_connected)
                {
                    _connected = true;
                    _log("Steam session with " + from + ": " + SteamP2P.DescribeRoute(from));
                    PeerConnected?.Invoke(PeerId.Server);
                }
                return;
            }

            var peer = _isServer ? EnsurePeer(from) : PeerId.Server;
            Received?.Invoke(peer, _buffer, 0, length, channel);
        }

        private PeerId EnsurePeer(ulong steamId)
        {
            if (_peerBySteam.TryGetValue(steamId, out var existing)) return new PeerId(existing);
            var number = _nextPeer++;
            _peerBySteam[steamId] = number;
            _steamByPeer[number] = steamId;
            _log("Steam player " + steamId + " joined: " + SteamP2P.DescribeRoute(steamId));
            var peer = new PeerId(number);
            PeerConnected?.Invoke(peer);
            return peer;
        }

        /// <summary>Knock until the host answers, then stop. Nothing else can start the conversation.</summary>
        private void KeepJoining()
        {
            if (_connected) return;
            var now = DateTime.UtcNow;
            if (now >= _giveUpAt)
            {
                IsRunning = false;
                PeerDisconnected?.Invoke(PeerId.Server, "No answer over Steam. Is the host running, and have "
                                                       + "they added your Steam ID to their game?");
                return;
            }
            if (now < _nextProbeAt) return;
            _nextProbeAt = now.AddSeconds(ProbeIntervalSeconds);
            SendTo(_hostSteamId, new[] { Probe }, 0, 1, Channel.Control, Delivery.ReliableOrdered);
        }

        private void DropSilentPeers()
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-PeerSilenceSeconds);
            List<ulong> gone = null;
            foreach (var pair in _lastHeard)
                if (pair.Value < cutoff && _peerBySteam.ContainsKey(pair.Key))
                {
                    if (gone == null) gone = new List<ulong>();
                    gone.Add(pair.Key);
                }
            if (gone == null) return;
            foreach (var id in gone)
            {
                var number = _peerBySteam[id];
                _peerBySteam.Remove(id);
                _steamByPeer.Remove(number);
                _lastHeard.Remove(id);
                SteamNative.SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(SteamP2P.Networking, id);
                PeerDisconnected?.Invoke(new PeerId(number), "Timeout");
            }
        }

        public void Send(PeerId to, byte[] data, int offset, int length, Channel channel, Delivery delivery)
        {
            if (!IsRunning) return;
            var steamId = SteamIdFor(to);
            if (steamId != 0) SendTo(steamId, data, offset, length, channel, delivery);
        }

        private ulong SteamIdFor(PeerId peer)
        {
            if (peer.IsServer) return _hostSteamId;
            return _steamByPeer.TryGetValue(peer.Value, out var id) ? id : 0;
        }

        private void SendTo(ulong steamId, byte[] data, int offset, int length, Channel channel, Delivery delivery)
        {
            // Steam takes a buffer start rather than an offset, so a slice has to be copied out.
            byte[] payload;
            if (offset == 0 && length == data.Length) payload = data;
            else
            {
                payload = new byte[length];
                Buffer.BlockCopy(data, offset, payload, 0, length);
            }
            var send = delivery == Delivery.Unreliable || delivery == Delivery.Sequenced
                ? SteamNative.SendUnreliable
                : SteamNative.SendReliable;
            SteamNative.SteamAPI_ISteamNetworking_SendP2PPacket(
                SteamP2P.Networking, steamId, payload, (uint)length, send, (int)channel);
        }

        public void Disconnect(PeerId peer, string reason)
        {
            var steamId = SteamIdFor(peer);
            if (steamId == 0) return;
            SteamNative.SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(SteamP2P.Networking, steamId);
            if (_peerBySteam.TryGetValue(steamId, out var number))
            {
                _peerBySteam.Remove(steamId);
                _steamByPeer.Remove(number);
            }
            PeerDisconnected?.Invoke(peer, reason);
        }

        /// <summary>The legacy Steam API does not report round-trip time, so the debug window shows nothing.</summary>
        public int GetPeerPingMs(PeerId peer) => 0;

        public void Dispose() => Stop();
    }
}
