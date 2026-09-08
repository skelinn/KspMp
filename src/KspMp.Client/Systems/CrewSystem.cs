using System;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Systems
{
    /// <summary>
    /// The owner's half of getting in and out of a craft. Another player pressed EVA or Board on a vessel we
    /// simulate; they cannot change its crew themselves, so they told us and we do it here. The modified
    /// snapshot that KSP fires afterwards is what tells everybody else.
    /// </summary>
    public sealed class CrewSystem : SystemBase
    {
        public CrewSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Crew";
        public int Applied { get; private set; }

        public override bool ShouldRun(GameScenes scene, bool connected) => connected && scene == GameScenes.FLIGHT;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.CrewEva, OnEva);
            Net.RegisterHandler(MessageId.CrewBoard, OnBoard);
            GameEvents.onAttemptEva.Add(OnAttemptEva);
            GameEvents.onCrewOnEva.Add(OnCrewOnEva);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.CrewEva, OnEva);
            Net.UnregisterHandler(MessageId.CrewBoard, OnBoard);
            GameEvents.onAttemptEva.Remove(OnAttemptEva);
            GameEvents.onCrewOnEva.Remove(OnCrewOnEva);
        }

        /// <summary>
        /// You may not put somebody else's kerbal out of an airlock, and you may not take any kerbal out of a
        /// craft you are not simulating - the vessel would change under its owner's feet. Setting
        /// <c>FlightEVA.overrideEVA</c> is KSP's own way of saying no: it is cleared immediately before
        /// onAttemptEva fires and checked immediately after.
        /// </summary>
        private void OnAttemptEva(ProtoCrewMember crew, Part part, UnityEngine.Transform airlock)
        {
            var eva = FlightEVA.fetch;
            if (eva == null || crew == null) return;
            if (Addon.Roster != null && Addon.Roster.IsOtherPlayersAvatar(crew.name))
            {
                Refuse(eva, crew.name + " is another player's Kerbal");
                return;
            }
            var vessel = part != null ? part.vessel : null;
            if (vessel == null || !Addon.Vessels.IsOwnedByOther(vessel.id)) return;
            // Our own avatar may still leave - the owner does the removing, see OnCrewOnEva - but nobody else's.
            if (Addon.Roster == null || Addon.Roster.AvatarName != crew.name)
                Refuse(eva, "only " + NameOf(Addon.Vessels.OwnerOf(vessel.id)) + " can move crew on " + vessel.GetDisplayName());
        }

        private static void Refuse(FlightEVA eva, string reason)
        {
            eva.overrideEVA = true;
            Log.Info("Refused an EVA: " + reason);
            ScreenMessages.PostScreenMessage(reason, 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>Our avatar climbed out of a craft somebody else simulates: tell them to remove the crew.</summary>
        private void OnCrewOnEva(GameEvents.FromToAction<Part, Part> action)
        {
            var from = action.from != null ? action.from.vessel : null;
            var to = action.to != null ? action.to.vessel : null;
            if (from == null || to == null || !Addon.Vessels.IsOwnedByOther(from.id)) return;
            var crew = to.GetVesselCrew();
            if (crew == null || crew.Count == 0 || crew[0] == null) return;
            Net.Send(MessageId.CrewEva, new CrewEvaMsg
            {
                FromVesselId = from.id,
                KerbalName = crew[0].name,
                EvaVesselId = to.id,
            }, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Told " + from.GetDisplayName() + "'s owner that " + crew[0].name + " has left it");
        }

        private void OnEva(NetDataReader body)
        {
            var msg = Envelope.Read<CrewEvaMsg>(body);
            var vessel = FlightGlobals.FindVessel(msg.FromVesselId);
            if (vessel == null || !Addon.Vessels.IsMine(msg.FromVesselId)) return;
            try
            {
                var part = FindPartWith(vessel, msg.KerbalName, out var crew);
                if (part == null)
                {
                    Log.Warn("Cannot take " + msg.KerbalName + " out of " + vessel.GetDisplayName() + ": they are not aboard");
                    return;
                }
                part.RemoveCrewmember(crew);
                crew.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                Vessel.CrewWasModified(vessel);
                Applied++;
                Log.Info(msg.KerbalName + " left " + vessel.GetDisplayName() + " on EVA (" + NameOf(msg.FromClientId) + " asked)");
            }
            catch (Exception e)
            {
                Log.Exception("Taking " + msg.KerbalName + " out of " + vessel.GetDisplayName(), e);
            }
        }

        private void OnBoard(NetDataReader body)
        {
            var msg = Envelope.Read<CrewBoardMsg>(body);
            var vessel = FlightGlobals.FindVessel(msg.ToVesselId);
            if (vessel == null || !Addon.Vessels.IsMine(msg.ToVesselId)) return;
            try
            {
                var part = FindPart(vessel, msg.PartFlightId);
                if (part == null)
                {
                    Log.Warn("Cannot seat " + msg.KerbalName + " on " + vessel.GetDisplayName() + ": part " + msg.PartFlightId + " is gone");
                    return;
                }
                var crew = HighLogic.CurrentGame.CrewRoster[msg.KerbalName];
                if (crew == null)
                {
                    Log.Warn("Cannot seat " + msg.KerbalName + ": they are not in our roster");
                    return;
                }
                if (FindPartWith(vessel, msg.KerbalName, out _) != null)
                {
                    // Already aboard here (a repeated report): nothing to do, and nothing to complain about.
                    Log.Info(msg.KerbalName + " is already aboard " + vessel.GetDisplayName());
                    return;
                }
                // The seat they took on their machine first; failing that (a seat our copy has filled
                // differently) any seat in the part, then any seat on the craft. A kerbal seated nowhere
                // here is a kerbal our next snapshot removes from their copy.
                var seated = msg.SeatIndex >= 0 && part.AddCrewmemberAt(crew, msg.SeatIndex);
                if (!seated) seated = part.AddCrewmemberAt(crew, FirstFreeSeat(part));
                if (!seated)
                {
                    for (var i = 0; i < vessel.parts.Count && !seated; i++)
                    {
                        var other = vessel.parts[i];
                        if (other == part || other.CrewCapacity <= 0) continue;
                        seated = other.AddCrewmemberAt(crew, FirstFreeSeat(other));
                        if (seated) Log.Info(msg.KerbalName + " took a seat in " + other.partInfo.title + " instead: " + part.partInfo.title + " is full here");
                    }
                }
                if (!seated)
                {
                    Log.Warn("Cannot seat " + msg.KerbalName + " on " + vessel.GetDisplayName() + ": no free seat anywhere aboard");
                    return;
                }
                crew.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                Vessel.CrewWasModified(vessel);
                Applied++;
                Log.Info(msg.KerbalName + " boarded " + vessel.GetDisplayName() + " (" + NameOf(msg.FromClientId) + " asked)");
            }
            catch (Exception e)
            {
                Log.Exception("Seating " + msg.KerbalName + " on " + vessel.GetDisplayName(), e);
            }
        }

        private static Part FindPartWith(Vessel vessel, string kerbalName, out ProtoCrewMember crew)
        {
            crew = null;
            if (vessel.parts == null) return null;
            for (var i = 0; i < vessel.parts.Count; i++)
            {
                var part = vessel.parts[i];
                if (part.protoModuleCrew == null) continue;
                for (var c = 0; c < part.protoModuleCrew.Count; c++)
                    if (part.protoModuleCrew[c] != null && part.protoModuleCrew[c].name == kerbalName)
                    {
                        crew = part.protoModuleCrew[c];
                        return part;
                    }
            }
            return null;
        }

        private static Part FindPart(Vessel vessel, uint flightId)
        {
            if (vessel.parts == null) return null;
            for (var i = 0; i < vessel.parts.Count; i++)
                if (vessel.parts[i].flightID == flightId) return vessel.parts[i];
            return null;
        }

        private static int FirstFreeSeat(Part part)
        {
            for (var seat = 0; seat < part.CrewCapacity; seat++)
            {
                var taken = false;
                if (part.protoModuleCrew != null)
                    for (var c = 0; c < part.protoModuleCrew.Count; c++)
                        if (part.protoModuleCrew[c] != null && part.protoModuleCrew[c].seatIdx == seat) { taken = true; break; }
                if (!taken) return seat;
            }
            return 0;
        }

        private string NameOf(int clientId) => Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;
    }
}
