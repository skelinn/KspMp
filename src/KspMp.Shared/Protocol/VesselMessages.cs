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
        /// <summary>The previous owner gave it to this player.</summary>
        HandedOver = 6,
        /// <summary>The owner's avatar left the vessel, so someone else aboard and flying it took over.</summary>
        PilotLeft = 7,
        /// <summary>Refused: the intended owner is not in flight on this vessel, so nobody would simulate it.</summary>
        NotInFlight = 8,
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
        /// <summary>For a Created snapshot: the vessel this one was undocked or decoupled from, else empty.</summary>
        public Guid SplitFrom;
        /// <summary>Which authority decision OwnerClientId came from; see <see cref="AuthorityAssignMsg.AuthoritySeq"/>.</summary>
        public uint AuthoritySeq;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(PersistentId);
            w.Put(OwnerClientId);
            w.Put((byte)Reason);
            w.Put(Name ?? string.Empty);
            w.Put(VesselType ?? string.Empty);
            w.PutBlob(ProtoDeflated ?? Array.Empty<byte>());
            w.Put(AuthoritySeq);
            w.PutGuidRaw(SplitFrom);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            PersistentId = r.GetUInt();
            OwnerClientId = r.GetInt();
            Reason = (ProtoReason)r.GetByte();
            Name = r.GetString();
            VesselType = r.GetString();
            ProtoDeflated = r.GetBlob();
            AuthoritySeq = r.GetUInt();
            SplitFrom = r.GetGuidRaw();
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
        /// <summary>
        /// Counts the server's authority decisions for this vessel. Ownership rides on three different messages
        /// travelling on two channels that are ordered independently of each other, so a bulky snapshot carrying
        /// the previous owner can arrive after the assignment that replaced it. Clients keep the highest sequence
        /// seen and ignore anything older, which is what stops a stale snapshot silently un-assigning a vessel.
        /// </summary>
        public uint AuthoritySeq;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(OwnerClientId);
            w.Put((byte)Reason);
            w.Put(AuthoritySeq);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            OwnerClientId = r.GetInt();
            Reason = (AuthorityReason)r.GetByte();
            AuthoritySeq = r.GetUInt();
        }
    }

    /// <summary>
    /// Resource amounts on a vessel, from its physics owner to everyone aboard it. Usually a delta: only the
    /// parts and resources that moved since the last one, with everything sent again every so often.
    /// </summary>
    public struct VesselResourcesMsg : INetSerializable
    {
        public struct Resource
        {
            public string Name;
            public float Amount;
        }

        public struct PartResources
        {
            public uint PartFlightId;
            public Resource[] Resources;
        }

        public Guid VesselId;
        public PartResources[] Parts;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            var parts = Parts ?? Array.Empty<PartResources>();
            w.Put((ushort)parts.Length);
            foreach (var part in parts)
            {
                w.Put(part.PartFlightId);
                var resources = part.Resources ?? Array.Empty<Resource>();
                w.Put((byte)resources.Length);
                foreach (var resource in resources)
                {
                    w.Put(resource.Name ?? string.Empty);
                    w.Put(resource.Amount);
                }
            }
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            Parts = new PartResources[r.GetUShort()];
            for (var i = 0; i < Parts.Length; i++)
            {
                Parts[i].PartFlightId = r.GetUInt();
                Parts[i].Resources = new Resource[r.GetByte()];
                for (var j = 0; j < Parts[i].Resources.Length; j++)
                {
                    Parts[i].Resources[j].Name = r.GetString();
                    Parts[i].Resources[j].Amount = r.GetFloat();
                }
            }
        }
    }
}
