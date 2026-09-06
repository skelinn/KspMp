using System;
using KspMp.Shared.Protocol;

namespace KspMp.Vessels
{
    /// <summary>What this client knows about one vessel of the shared universe.</summary>
    public sealed class RemoteVessel
    {
        public Guid Id;
        public uint PersistentId;
        public string Name = "";
        public string VesselType = "";
        /// <summary>Client id of the physics owner, 0 = nobody. Write it through <see cref="VesselRegistry.ApplyOwner"/>.</summary>
        public int OwnerClientId;
        /// <summary>Which server authority decision <see cref="OwnerClientId"/> came from; older ones are ignored.</summary>
        public uint AuthoritySeq;
        /// <summary>Latest snapshot from the server (null when only this client knows the vessel so far).</summary>
        public byte[] ProtoDeflated;
        /// <summary>A snapshot arrived that is not applied to the game yet.</summary>
        public bool ProtoDirty;
        public bool HasState;
        public VesselStateMsg LastState;
        /// <summary>Present while another client simulates the vessel and it exists in our game.</summary>
        public Replica Replica;
        public float LastProtoSentAt;
        public float LastStateSentAt;
        /// <summary>Set when we are handed the vessel, cleared once we have actually sent state for it.</summary>
        public bool AnnounceFirstStateSent;

        public string ShortId => Id.ToString().Substring(0, 8);
        public string Label => (string.IsNullOrEmpty(Name) ? "vessel" : Name) + " " + ShortId;
    }
}
