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
}
