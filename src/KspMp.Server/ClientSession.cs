using System;
using KspMp.Shared.Protocol;

namespace KspMp.Server
{
    public sealed class ClientSession
    {
        public PeerId Peer;
        public int ClientId;
        public Guid PlayerId;
        public string PlayerName;
        public bool Handshaken;
        /// <summary>Hello was refused; a disconnect is pending and further messages are ignored.</summary>
        public bool Rejected;
        public DateTime ConnectedAtUtc = DateTime.UtcNow;

        public string DisplayName => Handshaken ? PlayerName + "#" + ClientId : Peer.ToString();
    }
}
