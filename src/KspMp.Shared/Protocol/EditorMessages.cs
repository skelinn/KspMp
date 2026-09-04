using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    public enum EditorFacilityKind : byte
    {
        Vab = 0,
        Sph = 1,
    }

    /// <summary>A player entered an editor and wants to build together with anyone else in the same facility.</summary>
    public struct EditorJoinMsg : INetSerializable
    {
        public EditorFacilityKind Facility;

        public void Serialize(NetDataWriter w) => w.Put((byte)Facility);
        public void Deserialize(NetDataReader r) => Facility = (EditorFacilityKind)r.GetByte();
    }

    public struct EditorLeaveMsg : INetSerializable
    {
        public int ClientId;

        public void Serialize(NetDataWriter w) => w.Put(ClientId);
        public void Deserialize(NetDataReader r) => ClientId = r.GetInt();
    }

    /// <summary>
    /// The whole craft as a deflated ConfigNode. Revision increases with every accepted change; a client that
    /// sends a snapshot built on an older revision is told the current one instead (last writer wins per revision).
    /// </summary>
    public struct EditorSnapshotMsg : INetSerializable
    {
        public EditorFacilityKind Facility;
        public int FromClientId;
        public int Revision;
        public string ShipName;
        public int PartCount;
        public byte[] CraftDeflated;
        public byte[] ManifestDeflated;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Facility);
            w.Put(FromClientId);
            w.Put(Revision);
            w.Put(ShipName ?? string.Empty);
            w.Put(PartCount);
            w.PutBytesWithLength(CraftDeflated ?? Array.Empty<byte>());
            w.PutBytesWithLength(ManifestDeflated ?? Array.Empty<byte>());
        }

        public void Deserialize(NetDataReader r)
        {
            Facility = (EditorFacilityKind)r.GetByte();
            FromClientId = r.GetInt();
            Revision = r.GetInt();
            ShipName = r.GetString();
            PartCount = r.GetInt();
            CraftDeflated = r.GetBytesWithLength();
            ManifestDeflated = r.GetBytesWithLength();
        }
    }

    /// <summary>Where another builder's cursor is and what they are holding, so their work is visible while they do it.</summary>
    public struct EditorPresenceMsg : INetSerializable
    {
        public EditorFacilityKind Facility;
        public int ClientId;
        public bool Holding;
        public string HeldPartName;
        public float CursorX, CursorY, CursorZ;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Facility);
            w.Put(ClientId);
            w.Put(Holding);
            w.Put(HeldPartName ?? string.Empty);
            w.Put(CursorX); w.Put(CursorY); w.Put(CursorZ);
        }

        public void Deserialize(NetDataReader r)
        {
            Facility = (EditorFacilityKind)r.GetByte();
            ClientId = r.GetInt();
            Holding = r.GetBool();
            HeldPartName = r.GetString();
            CursorX = r.GetFloat(); CursorY = r.GetFloat(); CursorZ = r.GetFloat();
        }
    }

    /// <summary>The shared craft was launched: everyone in the session leaves the editor, and seated players join the flight.</summary>
    public struct EditorLaunchMsg : INetSerializable
    {
        public EditorFacilityKind Facility;
        public int FromClientId;
        public string ShipName;
        public string LaunchSite;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Facility);
            w.Put(FromClientId);
            w.Put(ShipName ?? string.Empty);
            w.Put(LaunchSite ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            Facility = (EditorFacilityKind)r.GetByte();
            FromClientId = r.GetInt();
            ShipName = r.GetString();
            LaunchSite = r.GetString();
        }
    }
}
