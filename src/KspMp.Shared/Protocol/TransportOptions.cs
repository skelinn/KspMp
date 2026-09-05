namespace KspMp.Shared.Protocol
{
    public sealed class TransportOptions
    {
        public bool IsServer;
        /// <summary>Client: server address. Server: ignored (binds all interfaces).</summary>
        public string Address = "127.0.0.1";
        /// <summary>Client: server port. Server: port to bind, 0 = any free port.</summary>
        public int Port = 7777;
        public string ConnectionKey = "KspMp";
        public int MaxPeers = 16;
        public int DisconnectTimeoutMs = 10000;
        public int UpdateTimeMs = 15;

        /// <summary>
        /// "host:port" of an introducer that brokers NAT hole punching, or empty to connect directly.
        /// Two machines behind home routers cannot reach each other to begin with, so both talk to a third
        /// party on a public address which sees their real external endpoints and tells each about the other.
        /// It only brokers the handshake; the game traffic that follows is peer to peer.
        /// </summary>
        public string Introducer = "";
        /// <summary>The code a server registers under, and a client asks for. Empty means no hole punching.</summary>
        public string JoinCode = "";
        /// <summary>How long a client waits to be introduced before giving up.</summary>
        public int PunchTimeoutMs = 12000;

        public bool UsesIntroducer => !string.IsNullOrEmpty(Introducer) && !string.IsNullOrEmpty(JoinCode);
    }
}
