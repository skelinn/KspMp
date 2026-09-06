using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    /// <summary>
    /// A player climbs out of somebody else's craft. Only the client simulating that craft may take a kerbal out
    /// of it, so the player who pressed the button reports what they did and the owner does it locally; the
    /// snapshot that follows tells everyone else.
    /// </summary>
    public struct CrewEvaMsg : INetSerializable
    {
        public Guid FromVesselId;
        public string KerbalName;
        /// <summary>The EVA vessel the kerbal became, so the owner can tell the two apart.</summary>
        public Guid EvaVesselId;
        public int FromClientId;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(FromVesselId);
            w.Put(KerbalName ?? string.Empty);
            w.PutGuidRaw(EvaVesselId);
            w.Put(FromClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            FromVesselId = r.GetGuidRaw();
            KerbalName = r.GetString();
            EvaVesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
        }
    }

    /// <summary>The other half: a kerbal climbing back into somebody else's craft, into a named part and seat.</summary>
    public struct CrewBoardMsg : INetSerializable
    {
        public Guid ToVesselId;
        public uint PartFlightId;
        /// <summary>Seat to sit in, or -1 for the first free one.</summary>
        public int SeatIndex;
        public string KerbalName;
        public Guid EvaVesselId;
        public int FromClientId;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(ToVesselId);
            w.Put(PartFlightId);
            w.Put(SeatIndex);
            w.Put(KerbalName ?? string.Empty);
            w.PutGuidRaw(EvaVesselId);
            w.Put(FromClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            ToVesselId = r.GetGuidRaw();
            PartFlightId = r.GetUInt();
            SeatIndex = r.GetInt();
            KerbalName = r.GetString();
            EvaVesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
        }
    }

    /// <summary>
    /// What a kerbal's jetpack is doing, from its owner, ten times a second while it is deployed. The six
    /// numbers are the thrust the kerbal is commanding in its own frame, linear and rotational, scaled to
    /// -127..127; the receiver lights the same plumes KSP would.
    /// </summary>
    public struct EvaFxMsg : INetSerializable
    {
        public const byte JetpackDeployed = 1;
        public const byte HasFuel = 2;
        public const byte Ragdoll = 4;

        public Guid VesselId;
        public byte Flags;
        public sbyte LinX, LinY, LinZ, RotX, RotY, RotZ;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(Flags);
            w.Put(LinX); w.Put(LinY); w.Put(LinZ);
            w.Put(RotX); w.Put(RotY); w.Put(RotZ);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            Flags = r.GetByte();
            LinX = r.GetSByte(); LinY = r.GetSByte(); LinZ = r.GetSByte();
            RotX = r.GetSByte(); RotY = r.GetSByte(); RotZ = r.GetSByte();
        }
    }
}
