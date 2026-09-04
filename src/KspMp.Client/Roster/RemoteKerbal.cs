using System;

namespace KspMp.Roster
{
    /// <summary>What this client knows about one kerbal of the shared roster.</summary>
    public sealed class RemoteKerbal
    {
        public string Name = "";
        public string NodeText = "";
        public bool IsAvatar;
        public Guid AvatarPlayerId;
        public int AvatarClientId;
        public byte Status;
        public double InactiveTimeEnd;
        /// <summary>Arrived but not applied to the game roster yet.</summary>
        public bool Dirty;
    }
}
