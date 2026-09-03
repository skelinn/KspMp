using System;
using System.Linq;
using System.Text;
using KspMp.Server;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Spike
{
    /// <summary>
    /// M0 spike: proves that LiteNetLib (compiled for net472) and DeflateStream work inside KSP's Mono runtime.
    /// Hosts a <see cref="ServerCore"/> in-process on a random loopback port, connects a client transport to it and
    /// runs Hello -> Welcome -> Ping -> Pong. Removed once M1 replaces it with the real connection flow.
    /// </summary>
    public sealed class NetSpike
    {
        public string DeflateStatus { get; private set; } = "pending";
        public string NetStatus { get; private set; } = "pending";
        public bool Done { get; private set; }

        private ServerCore _server;
        private LiteNetLibTransport _serverTransport;
        private LiteNetLibTransport _client;
        private readonly NetDataWriter _writer = new NetDataWriter();
        private float _startedAt;
        private bool _stopped;

        public void Begin()
        {
            RunDeflateTest();
            try
            {
                _serverTransport = new LiteNetLibTransport(new TransportOptions { IsServer = true, Port = 0 }, m => Log.Info("[spike server] " + m));
                _server = new ServerCore(_serverTransport, m => Log.Info("[spike server] " + m));
                _server.Start();
                var port = _serverTransport.LocalPort;

                _client = new LiteNetLibTransport(new TransportOptions { IsServer = false, Address = "127.0.0.1", Port = port }, m => Log.Info("[spike client] " + m));
                _client.PeerConnected += OnConnected;
                _client.PeerDisconnected += (peer, reason) => { if (!Done) Fail("disconnected: " + reason); };
                _client.Received += OnReceived;
                _client.Start();

                _startedAt = Time.realtimeSinceStartup;
                NetStatus = "connecting to 127.0.0.1:" + port;
            }
            catch (Exception e)
            {
                Log.Exception("Starting loopback spike", e);
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        private void RunDeflateTest()
        {
            try
            {
                var text = new StringBuilder();
                for (var i = 0; i < 300; i++)
                    text.Append("PART\n{\n\tname = mk1pod.v2\n\tcid = ").Append(4291234567u + (uint)i).Append("\n\tuid = ").Append(i).Append("\n}\n");
                var raw = Encoding.UTF8.GetBytes(text.ToString());
                var packed = DeflateCodec.Compress(raw, 0, raw.Length);
                var back = DeflateCodec.Decompress(packed, 0, packed.Length);
                var ok = back.SequenceEqual(raw);
                DeflateStatus = (ok ? "OK" : "MISMATCH") + " (" + raw.Length + " -> " + packed.Length + " bytes)";
                Log.Info("Deflate round trip: " + DeflateStatus);
            }
            catch (Exception e)
            {
                DeflateStatus = "FAILED: " + e.Message;
                Log.Exception("Deflate round trip", e);
            }
        }

        private void OnConnected(PeerId peer)
        {
            Log.Info("Spike: connected to loopback server, sending Hello");
            var settings = KspMpAddon.Instance.Settings;
            Send(MessageId.Hello, new HelloMsg
            {
                ProtocolVersion = ProtocolVersion.Current,
                ModVersion = KspMpAddon.Version,
                PlayerId = settings.PlayerId,
                PlayerName = settings.PlayerName,
                KspVersion = Versioning.GetVersionString(),
            }, Channel.Control, Delivery.ReliableOrdered);
        }

        private void OnReceived(PeerId from, byte[] buffer, int offset, int length, Channel channel)
        {
            var reader = new NetDataReader(buffer, offset, length);
            if (!Envelope.TryReadHeader(reader, out var id, out var flags, out _)) return;
            var body = Envelope.OpenBody(reader, flags);
            switch (id)
            {
                case MessageId.Welcome:
                {
                    var welcome = Envelope.Read<WelcomeMsg>(body);
                    Log.Info("Spike: welcomed as client #" + welcome.ClientId + ", sending Ping");
                    Send(MessageId.Ping, new PingMsg { ClientTicks = DateTime.UtcNow.Ticks }, Channel.State, Delivery.Sequenced);
                    break;
                }
                case MessageId.Pong:
                {
                    var pong = Envelope.Read<PongMsg>(body);
                    var rttMs = (DateTime.UtcNow.Ticks - pong.ClientTicks) / 10000.0;
                    NetStatus = "OK (hello/welcome/ping/pong over loopback, rtt " + rttMs.ToString("F1") + " ms)";
                    Log.Info("Spike: " + NetStatus);
                    Done = true;
                    break;
                }
                case MessageId.Reject:
                    Fail("rejected: " + Envelope.Read<RejectMsg>(body).Reason);
                    break;
            }
        }

        private void Send<T>(MessageId id, T message, Channel channel, Delivery delivery) where T : INetSerializable
        {
            Envelope.Write(_writer, id, message);
            _client.Send(PeerId.Server, _writer.Data, 0, _writer.Length, channel, delivery);
        }

        public void Update()
        {
            if (_stopped) return;
            if (Done)
            {
                Shutdown();
                return;
            }
            if (_server == null || _client == null) return;
            try
            {
                _server.Poll();
                _client.Poll();
            }
            catch (Exception e)
            {
                Log.Exception("Spike poll", e);
                Fail(e.GetType().Name + ": " + e.Message);
            }
            if (!Done && Time.realtimeSinceStartup - _startedAt > 10f) Fail("timed out after 10 s");
            if (Done) Shutdown();
        }

        private void Fail(string why)
        {
            NetStatus = "FAILED: " + why;
            Log.Error("Spike: " + NetStatus);
            Done = true;
        }

        public void Shutdown()
        {
            if (_stopped) return;
            _stopped = true;
            try
            {
                _client?.Stop();
                _server?.Stop();
            }
            catch (Exception e)
            {
                Log.Exception("Spike shutdown", e);
            }
        }
    }
}
