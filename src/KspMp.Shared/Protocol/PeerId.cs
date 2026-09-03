using System;

namespace KspMp.Shared.Protocol
{
    /// <summary>
    /// Transport-level identity of a connected peer. On a client the server is always <see cref="Server"/>.
    /// <c>default(PeerId)</c> is <see cref="None"/>; peer numbers (0-based, as LiteNetLib assigns them) are stored offset by one.
    /// </summary>
    public readonly struct PeerId : IEquatable<PeerId>
    {
        public static readonly PeerId None = default(PeerId);
        public static readonly PeerId Server = new PeerId(-1, true);

        private readonly int _raw; // 0 = none, -1 = server, n + 1 = peer n

        public PeerId(int peerNumber)
        {
            if (peerNumber < 0) throw new ArgumentOutOfRangeException(nameof(peerNumber));
            _raw = peerNumber + 1;
        }

        private PeerId(int raw, bool _)
        {
            _raw = raw;
        }

        public bool IsNone => _raw == 0;
        public bool IsServer => _raw == -1;
        /// <summary>The peer number (0-based); -1 for <see cref="None"/>, -2 for <see cref="Server"/>.</summary>
        public int Value => _raw - 1;

        public bool Equals(PeerId other) => _raw == other._raw;
        public override bool Equals(object obj) => obj is PeerId other && Equals(other);
        public override int GetHashCode() => _raw;
        public override string ToString() => IsServer ? "server" : IsNone ? "none" : "peer#" + Value;

        public static bool operator ==(PeerId a, PeerId b) => a._raw == b._raw;
        public static bool operator !=(PeerId a, PeerId b) => a._raw != b._raw;
    }
}
