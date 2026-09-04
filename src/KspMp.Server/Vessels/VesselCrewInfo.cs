using System;
using System.Collections.Generic;
using KspMp.Shared.Config;

namespace KspMp.Server.Vessels
{
    /// <summary>Crew placement read from a vessel snapshot: which kerbals sit in which part, and the reference (command) part.</summary>
    public sealed class VesselCrewInfo
    {
        public sealed class PartCrew
        {
            public uint FlightId;
            public bool IsCommandPart;
            public List<string> Crew = new List<string>();
        }

        public uint ReferencePartId;
        public List<PartCrew> Parts = new List<PartCrew>();

        public IEnumerable<string> AllCrew()
        {
            foreach (var part in Parts)
                foreach (var name in part.Crew)
                    yield return name;
        }

        public static VesselCrewInfo Parse(string vesselNodeText)
        {
            var info = new VesselCrewInfo();
            var root = CfgNode.Parse(vesselNodeText);
            var vessel = root.GetNode("VESSEL") ?? root;
            info.ReferencePartId = (uint)vessel.GetLong("ref", 0);
            foreach (var partNode in vessel.GetNodes("PART"))
            {
                var part = new PartCrew { FlightId = (uint)partNode.GetLong("uid", 0) };
                foreach (var module in partNode.GetNodes("MODULE"))
                    if (module.GetValue("name") == "ModuleCommand") part.IsCommandPart = true;
                foreach (var crew in partNode.GetValues("crew"))
                    if (!string.IsNullOrEmpty(crew)) part.Crew.Add(crew.Trim());
                if (part.Crew.Count > 0 || part.IsCommandPart) info.Parts.Add(part);
            }
            return info;
        }

        /// <summary>The command seat: seat 0 of the reference part, else seat 0 of the first command part with crew, else the first crew member anywhere.</summary>
        public string CommandSeatOccupant(Func<string, bool> isAvatar)
        {
            foreach (var part in Parts)
                if (part.FlightId == ReferencePartId && part.Crew.Count > 0 && isAvatar(part.Crew[0])) return part.Crew[0];
            foreach (var part in Parts)
                if (part.IsCommandPart)
                    foreach (var name in part.Crew)
                        if (isAvatar(name)) return name;
            foreach (var name in AllCrew())
                if (isAvatar(name)) return name;
            return null;
        }
    }
}
