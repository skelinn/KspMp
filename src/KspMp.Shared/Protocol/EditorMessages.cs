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
        /// <summary>Which bench this belongs to: the client id of its owner (0 = the sender's own).</summary>
        public int SessionOwnerClientId;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Facility);
            w.Put(FromClientId);
            w.Put(Revision);
            w.Put(ShipName ?? string.Empty);
            w.Put(PartCount);
            w.PutBlob(CraftDeflated ?? Array.Empty<byte>());
            w.PutBlob(ManifestDeflated ?? Array.Empty<byte>());
            w.Put(SessionOwnerClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            Facility = (EditorFacilityKind)r.GetByte();
            FromClientId = r.GetInt();
            Revision = r.GetInt();
            ShipName = r.GetString();
            PartCount = r.GetInt();
            CraftDeflated = r.GetBlob();
            ManifestDeflated = r.GetBlob();
            SessionOwnerClientId = r.GetInt();
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
        /// <summary>Which bench this belongs to: the client id of its owner (0 = the sender's own).</summary>
        public int SessionOwnerClientId;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Facility);
            w.Put(ClientId);
            w.Put(Holding);
            w.Put(HeldPartName ?? string.Empty);
            w.Put(CursorX); w.Put(CursorY); w.Put(CursorZ);
            w.Put(SessionOwnerClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            Facility = (EditorFacilityKind)r.GetByte();
            ClientId = r.GetInt();
            Holding = r.GetBool();
            HeldPartName = r.GetString();
            CursorX = r.GetFloat(); CursorY = r.GetFloat(); CursorZ = r.GetFloat();
            SessionOwnerClientId = r.GetInt();
        }
    }

    /// <summary>The shared craft was launched: everyone in the session leaves the editor, and seated players join the flight.</summary>
    public struct EditorLaunchMsg : INetSerializable
    {
        public EditorFacilityKind Facility;
        public int FromClientId;
        public string ShipName;
        public string LaunchSite;
        /// <summary>Kerbals seated at launch, so a player whose avatar is aboard can be invited into the flight.</summary>
        public string[] AboardKerbals;
        /// <summary>True when the launch came from an editor bench (false: the space centre's craft browser).</summary>
        public bool FromEditor;
        /// <summary>Which bench was launched: the client id of its owner (0 = the sender's own).</summary>
        public int SessionOwnerClientId;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Facility);
            w.Put(FromClientId);
            w.Put(ShipName ?? string.Empty);
            w.Put(LaunchSite ?? string.Empty);
            var count = AboardKerbals != null ? AboardKerbals.Length : 0;
            w.Put((byte)count);
            for (var i = 0; i < count; i++) w.Put(AboardKerbals[i] ?? string.Empty);
            w.Put(SessionOwnerClientId);
            w.Put(FromEditor);
        }

        public void Deserialize(NetDataReader r)
        {
            Facility = (EditorFacilityKind)r.GetByte();
            FromClientId = r.GetInt();
            ShipName = r.GetString();
            LaunchSite = r.GetString();
            var count = r.GetByte();
            AboardKerbals = new string[count];
            for (var i = 0; i < count; i++) AboardKerbals[i] = r.GetString();
            SessionOwnerClientId = r.GetInt();
            FromEditor = r.GetBool();
        }
    }

    /// <summary>One workbench: who owns it, what is on it, and who is building there.</summary>
    public struct EditorSessionInfo : INetSerializable
    {
        public int OwnerClientId;
        public EditorFacilityKind Facility;
        public string ShipName;
        public int PartCount;
        public int Revision;
        public int[] BuilderClientIds;

        public void Serialize(NetDataWriter w)
        {
            w.Put(OwnerClientId);
            w.Put((byte)Facility);
            w.Put(ShipName ?? string.Empty);
            w.Put(PartCount);
            w.Put(Revision);
            var count = BuilderClientIds != null ? BuilderClientIds.Length : 0;
            w.Put((byte)count);
            for (var i = 0; i < count; i++) w.Put(BuilderClientIds[i]);
        }

        public void Deserialize(NetDataReader r)
        {
            OwnerClientId = r.GetInt();
            Facility = (EditorFacilityKind)r.GetByte();
            ShipName = r.GetString();
            PartCount = r.GetInt();
            Revision = r.GetInt();
            var count = r.GetByte();
            BuilderClientIds = new int[count];
            for (var i = 0; i < count; i++) BuilderClientIds[i] = r.GetInt();
        }
    }

    /// <summary>Every open workbench. Sent to everyone, in every scene, so the players list can say who is building what.</summary>
    public struct EditorSessionListMsg : INetSerializable
    {
        public EditorSessionInfo[] Sessions;

        public void Serialize(NetDataWriter w)
        {
            var count = Sessions != null ? Sessions.Length : 0;
            w.Put((byte)count);
            for (var i = 0; i < count; i++) Sessions[i].Serialize(w);
        }

        public void Deserialize(NetDataReader r)
        {
            var count = r.GetByte();
            Sessions = new EditorSessionInfo[count];
            for (var i = 0; i < count; i++) Sessions[i].Deserialize(r);
        }
    }

    /// <summary>Join somebody's workbench, or 0 to go back to your own.</summary>
    public struct EditorSessionJoinMsg : INetSerializable
    {
        public int OwnerClientId;

        public void Serialize(NetDataWriter w) => w.Put(OwnerClientId);
        public void Deserialize(NetDataReader r) => OwnerClientId = r.GetInt();
    }
}
