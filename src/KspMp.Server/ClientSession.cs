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
        /// <summary>From Hello: the parts this install has, so a mismatch with the others can be said out loud.</summary>
        public int PartCount;
        public string PartsHash = "";
        public string KspVersion = "";
        public bool Handshaken;
        /// <summary>Hello was refused; a disconnect is pending and further messages are ignored.</summary>
        public bool Rejected;
        public DateTime ConnectedAtUtc = DateTime.UtcNow;
        /// <summary>When the last message came in: tells a live session from a dead one when the same player id shows up twice.</summary>
        public DateTime LastHeardUtc = DateTime.UtcNow;
        public string AvatarKerbalName = "";
        public PresenceMsg Presence;

        public bool HasAvatar => !string.IsNullOrEmpty(AvatarKerbalName);

        public bool IsOnline => Handshaken && !Rejected;
        public string DisplayName => Handshaken ? PlayerName + "#" + ClientId : Peer.ToString();
    }
}
