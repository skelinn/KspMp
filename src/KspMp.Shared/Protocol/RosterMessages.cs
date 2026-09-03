using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    public enum KerbalReason : byte
    {
        Sync = 0,
        Bootstrap = 1,
        Created = 2,
        Changed = 3,
        Avatar = 4,
    }

    /// <summary>Full ProtoCrewMember ConfigNode text. The server fills in the avatar fields from its player records.</summary>
    public struct KerbalProtoMsg : INetSerializable
    {
        public string Name;
        public KerbalReason Reason;
        public bool IsAvatar;
        public Guid AvatarPlayerId;
        public int AvatarClientId;
        public string NodeText;

        public void Serialize(NetDataWriter w)
        {
            w.Put(Name ?? string.Empty);
            w.Put((byte)Reason);
            w.Put(IsAvatar);
            w.PutGuidRaw(AvatarPlayerId);
            w.Put(AvatarClientId);
            w.Put(NodeText ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            Name = r.GetString();
            Reason = (KerbalReason)r.GetByte();
            IsAvatar = r.GetBool();
            AvatarPlayerId = r.GetGuidRaw();
            AvatarClientId = r.GetInt();
            NodeText = r.GetString();
        }
    }

    /// <summary>Roster status of one kerbal: 0 Available, 1 Assigned, 2 Dead, 3 Missing (KSP's RosterStatus order).</summary>
    public struct KerbalStatusMsg : INetSerializable
    {
        public string Name;
        public byte Status;
        public double InactiveTimeEnd;

        public void Serialize(NetDataWriter w)
        {
            w.Put(Name ?? string.Empty);
            w.Put(Status);
            w.Put(InactiveTimeEnd);
        }

        public void Deserialize(NetDataReader r)
        {
            Name = r.GetString();
            Status = r.GetByte();
            InactiveTimeEnd = r.GetDouble();
        }
    }

    public struct KerbalRemovedMsg : INetSerializable
    {
        public string Name;

        public void Serialize(NetDataWriter w) => w.Put(Name ?? string.Empty);
        public void Deserialize(NetDataReader r) => Name = r.GetString();
    }

    public struct AvatarClaimMsg : INetSerializable
    {
        public string KerbalName;
        public string Trait;

        public void Serialize(NetDataWriter w)
        {
            w.Put(KerbalName ?? string.Empty);
            w.Put(Trait ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            KerbalName = r.GetString();
            Trait = r.GetString();
        }
    }

    public struct AvatarClaimResultMsg : INetSerializable
    {
        public bool Ok;
        public string KerbalName;
        public string Trait;
        public string Reason;

        public void Serialize(NetDataWriter w)
        {
            w.Put(Ok);
            w.Put(KerbalName ?? string.Empty);
            w.Put(Trait ?? string.Empty);
            w.Put(Reason ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            Ok = r.GetBool();
            KerbalName = r.GetString();
            Trait = r.GetString();
            Reason = r.GetString();
        }
    }

    public enum PresenceState : byte
    {
        MissionControl = 0,
        InFlight = 1,
        OnEva = 2,
        Editor = 3,
    }

    /// <summary>Where a player is. Clients report their own; the server rebroadcasts everyone's.</summary>
    public struct PresenceMsg : INetSerializable
    {
        public int ClientId;
        public PresenceState State;
        public Guid VesselId;
        public string VesselName;
        public byte Scene;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ClientId);
            w.Put((byte)State);
            w.PutGuidRaw(VesselId);
            w.Put(VesselName ?? string.Empty);
            w.Put(Scene);
        }

        public void Deserialize(NetDataReader r)
        {
            ClientId = r.GetInt();
            State = (PresenceState)r.GetByte();
            VesselId = r.GetGuidRaw();
            VesselName = r.GetString();
            Scene = r.GetByte();
        }
    }
}
