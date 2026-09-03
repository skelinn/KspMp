using System;

namespace KspMp.Shared.Protocol
{
    /// <summary>Raw packet callback. The buffer is only valid during the call; copy it if you keep it.</summary>
    public delegate void ReceivedHandler(PeerId from, byte[] buffer, int offset, int length, Channel channel);

    /// <summary>
    /// Transport abstraction. Implementations: LiteNetLib UDP (client and server), loopback (tests, in-process host),
    /// later Steam relay. All callbacks are raised from <see cref="Poll"/> on the calling thread.
    /// </summary>
    public interface INetTransport : IDisposable
    {
        bool IsRunning { get; }

        /// <summary>Server: bind. Client: open a socket and start connecting.</summary>
        void Start();

        void Stop();

        /// <summary>Pump incoming packets and raise events. Call once per frame / tick.</summary>
        void Poll();

        void Send(PeerId to, byte[] data, int offset, int length, Channel channel, Delivery delivery);

        void Disconnect(PeerId peer, string reason);

        event Action<PeerId> PeerConnected;
        event Action<PeerId, string> PeerDisconnected;
        event ReceivedHandler Received;
    }
}
