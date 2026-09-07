using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    /// <summary>Two vessels with different physics owners are close: the server moves both under one owner.</summary>
    public struct DockIntentMsg : INetSerializable
    {
        public Guid MyVesselId;
        public Guid OtherVesselId;
        public float DistanceMeters;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(MyVesselId);
            w.PutGuidRaw(OtherVesselId);
            w.Put(DistanceMeters);
        }

        public void Deserialize(NetDataReader r)
        {
            MyVesselId = r.GetGuidRaw();
            OtherVesselId = r.GetGuidRaw();
            DistanceMeters = r.GetFloat();
        }
    }

    /// <summary>Docking finished on the owner: the merged vessel's snapshot plus the id of the vessel that ceased to exist.</summary>
    public struct DockCommitMsg : INetSerializable
    {
        public Guid SurvivorVesselId;
        public Guid RemovedVesselId;
        public int OwnerClientId;
        public string Name;
        public byte[] ProtoDeflated;
        /// <summary>Which authority decision OwnerClientId came from; see <see cref="AuthorityAssignMsg.AuthoritySeq"/>.</summary>
        public uint AuthoritySeq;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(SurvivorVesselId);
            w.PutGuidRaw(RemovedVesselId);
            w.Put(OwnerClientId);
            w.Put(Name ?? string.Empty);
            w.PutBlob(ProtoDeflated ?? Array.Empty<byte>());
            w.Put(AuthoritySeq);
        }

        public void Deserialize(NetDataReader r)
        {
            SurvivorVesselId = r.GetGuidRaw();
            RemovedVesselId = r.GetGuidRaw();
            OwnerClientId = r.GetInt();
            Name = r.GetString();
            ProtoDeflated = r.GetBlob();
            AuthoritySeq = r.GetUInt();
        }
    }
}
