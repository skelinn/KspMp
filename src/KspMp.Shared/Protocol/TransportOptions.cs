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
    }
}
