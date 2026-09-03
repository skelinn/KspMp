using System;

namespace KspMp.Shared.Protocol
{
    /// <summary>Transport-level identity of a connected peer. On a client the server is always <see cref="Server"/>.</summary>
    public readonly struct PeerId : IEquatable<PeerId>
    {
        public static readonly PeerId None = new PeerId(-1);
        public static readonly PeerId Server = new PeerId(-2);

        public readonly int Value;

        public PeerId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value == -1;
        public bool IsServer => Value == -2;

        public bool Equals(PeerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PeerId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsServer ? "server" : IsNone ? "none" : "peer#" + Value;

        public static bool operator ==(PeerId a, PeerId b) => a.Value == b.Value;
        public static bool operator !=(PeerId a, PeerId b) => a.Value != b.Value;
    }
}
