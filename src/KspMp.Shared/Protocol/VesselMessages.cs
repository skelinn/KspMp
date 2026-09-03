using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    /// <summary>Why a full vessel snapshot was sent.</summary>
    public enum ProtoReason : byte
    {
        Sync = 0,
        FlightReady = 1,
        Modified = 2,
        Periodic = 3,
        Created = 4,
        LeavingFlight = 5,
        OnRails = 6,
        Requested = 7,
    }

    public enum AuthorityReason : byte
    {
        Granted = 0,
        Denied = 1,
        Released = 2,
        OwnerLeft = 3,
        Created = 4,
        Removed = 5,
    }

    /// <summary>
    /// Full vessel snapshot: the ProtoVessel ConfigNode text, deflated by the sender. The server stores and relays
    /// the bytes untouched. OwnerClientId is filled in by the server (0 = nobody simulates it).
    /// </summary>
    public struct VesselProtoMsg : INetSerializable
    {
        public Guid VesselId;
        public uint PersistentId;
        public int OwnerClientId;
        public ProtoReason Reason;
        public string Name;
        public string VesselType;
        public byte[] ProtoDeflated;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(PersistentId);
            w.Put(OwnerClientId);
            w.Put((byte)Reason);
            w.Put(Name ?? string.Empty);
            w.Put(VesselType ?? string.Empty);
            w.PutBytesWithLength(ProtoDeflated ?? Array.Empty<byte>());
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            PersistentId = r.GetUInt();
            OwnerClientId = r.GetInt();
            Reason = (ProtoReason)r.GetByte();
            Name = r.GetString();
            VesselType = r.GetString();
            ProtoDeflated = r.GetBytesWithLength();
        }
    }

    public struct VesselRemoveMsg : INetSerializable
    {
        public Guid VesselId;
        public string Reason;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(Reason ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            Reason = r.GetString();
        }
    }

    /// <summary>
    /// Body-relative kinematic state of a vessel at universal time Ut, sent by its physics owner. Immune to the
    /// floating origin and Krakensbane shifts because nothing in it is a world coordinate.
    /// </summary>
    public struct VesselStateMsg : INetSerializable
    {
        public Guid VesselId;
        public double Ut;
        public ushort BodyIndex;
        public byte Situation;
        public bool Landed;
        public bool Splashed;
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public float HeightFromTerrain;
        public float SrfVelX, SrfVelY, SrfVelZ;
        public float RotX, RotY, RotZ, RotW;
        public float AngVelX, AngVelY, AngVelZ;
        public double Inclination;
        public double Eccentricity;
        public double SemiMajorAxis;
        public double Lan;
        public double ArgumentOfPeriapsis;
        public double MeanAnomalyAtEpoch;
        public double Epoch;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(Ut);
            w.Put(BodyIndex);
            w.Put(Situation);
            w.Put((byte)((Landed ? 1 : 0) | (Splashed ? 2 : 0)));
            w.Put(Latitude);
            w.Put(Longitude);
            w.Put(Altitude);
            w.Put(HeightFromTerrain);
            w.Put(SrfVelX); w.Put(SrfVelY); w.Put(SrfVelZ);
            w.Put(RotX); w.Put(RotY); w.Put(RotZ); w.Put(RotW);
            w.Put(AngVelX); w.Put(AngVelY); w.Put(AngVelZ);
            w.Put(Inclination);
            w.Put(Eccentricity);
            w.Put(SemiMajorAxis);
            w.Put(Lan);
            w.Put(ArgumentOfPeriapsis);
            w.Put(MeanAnomalyAtEpoch);
            w.Put(Epoch);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            Ut = r.GetDouble();
            BodyIndex = r.GetUShort();
            Situation = r.GetByte();
            var flags = r.GetByte();
            Landed = (flags & 1) != 0;
            Splashed = (flags & 2) != 0;
            Latitude = r.GetDouble();
            Longitude = r.GetDouble();
            Altitude = r.GetDouble();
            HeightFromTerrain = r.GetFloat();
            SrfVelX = r.GetFloat(); SrfVelY = r.GetFloat(); SrfVelZ = r.GetFloat();
            RotX = r.GetFloat(); RotY = r.GetFloat(); RotZ = r.GetFloat(); RotW = r.GetFloat();
            AngVelX = r.GetFloat(); AngVelY = r.GetFloat(); AngVelZ = r.GetFloat();
            Inclination = r.GetDouble();
            Eccentricity = r.GetDouble();
            SemiMajorAxis = r.GetDouble();
            Lan = r.GetDouble();
            ArgumentOfPeriapsis = r.GetDouble();
            MeanAnomalyAtEpoch = r.GetDouble();
            Epoch = r.GetDouble();
        }
    }

    public struct AuthorityRequestMsg : INetSerializable
    {
        public Guid VesselId;

        public void Serialize(NetDataWriter w) => w.PutGuidRaw(VesselId);
        public void Deserialize(NetDataReader r) => VesselId = r.GetGuidRaw();
    }

    public struct AuthorityReleaseMsg : INetSerializable
    {
        public Guid VesselId;

        public void Serialize(NetDataWriter w) => w.PutGuidRaw(VesselId);
        public void Deserialize(NetDataReader r) => VesselId = r.GetGuidRaw();
    }

    /// <summary>Server-decided physics owner of a vessel. OwnerClientId 0 = nobody (everyone propagates it on rails).</summary>
    public struct AuthorityAssignMsg : INetSerializable
    {
        public Guid VesselId;
        public int OwnerClientId;
        public AuthorityReason Reason;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(OwnerClientId);
            w.Put((byte)Reason);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            OwnerClientId = r.GetInt();
            Reason = (AuthorityReason)r.GetByte();
        }
    }
}
