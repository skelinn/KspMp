using System;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Vessels
{
    public sealed class VesselRecord
    {
        public Guid Id;
        public uint PersistentId;
        public string Name = "";
        public string VesselType = "";
        /// <summary>Deflated ProtoVessel ConfigNode text, exactly as the owner sent it.</summary>
        public byte[] ProtoDeflated = Array.Empty<byte>();
        public int Version;
        public double UpdatedUt;
        public bool Dirty;
        public bool HasState;
        public VesselStateMsg LastState;

        public VesselProtoMsg ToProtoMessage(int ownerClientId, ProtoReason reason) => new VesselProtoMsg
        {
            VesselId = Id,
            PersistentId = PersistentId,
            OwnerClientId = ownerClientId,
            Reason = reason,
            Name = Name,
            VesselType = VesselType,
            ProtoDeflated = ProtoDeflated,
        };
    }
}
