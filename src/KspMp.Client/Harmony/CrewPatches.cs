using System;
using HarmonyLib;
using KspMp.Shared.Protocol;

namespace KspMp.Harmony
{
    /// <summary>
    /// A kerbal on EVA is a vessel whose single part must carry a crew member: <c>ProtoPartSnapshot.Load</c>
    /// reads <c>protoModuleCrew[0]</c> without checking when the vessel is an EVA. That call happens inside
    /// <c>Vessel.Load()</c> as the kerbal comes into physics range, far below any try/catch of ours, so a
    /// crewless EVA snapshot takes the whole load path down with it.
    ///
    /// <see cref="Vessels.VesselLoader"/> already holds such a snapshot back, so this only fires if one gets
    /// through anyway - hence the loud log rather than a quiet skip.
    /// </summary>
    [HarmonyPatch(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.Load), typeof(Vessel), typeof(bool))]
    internal static class ProtoPartSnapshot_Load
    {
        private static bool Prefix(ProtoPartSnapshot __instance, Vessel vesselRef, ref Part __result)
        {
            if (vesselRef == null || !vesselRef.isEVA) return true;
            if (__instance.protoModuleCrew != null && __instance.protoModuleCrew.Count > 0 && __instance.protoModuleCrew[0] != null) return true;
            Log.Error("Refused to load an EVA part with no crew (vessel " + vesselRef.id.ToString().Substring(0, 8)
                      + "); this should have been held back before reaching KSP");
            __result = null;
            return false;
        }
    }

    /// <summary>
    /// Boarding somebody else's craft. The kerbal has to be put in a seat by whoever simulates that craft, so
    /// the boarding is reported and the owner does it; our own EVA vessel disappearing is already reported by
    /// the ordinary vessel-removal path.
    /// </summary>
    [HarmonyPatch(typeof(KerbalEVA), "proceedAndBoard")]
    internal static class KerbalEVA_ProceedAndBoard
    {
        // Read before KSP boards: by the time the method returns the kerbal has left its EVA vessel, and the
        // report found no kerbal to name and said nothing - the owner never seated the player, whose copy of
        // the craft was then rebuilt without them.
        private static void Prefix(KerbalEVA __instance, out BoardingInfo __state) => __state = BoardingInfo.Of(__instance);

        private static void Postfix(KerbalEVA __instance, Part p, BoardingInfo __state)
        {
            ReportBoarding(__state, p, seatIndex: -1);
        }

        internal struct BoardingInfo
        {
            public string KerbalName;
            public System.Guid EvaVesselId;

            public static BoardingInfo Of(KerbalEVA eva)
            {
                var info = new BoardingInfo();
                if (eva == null || eva.vessel == null) return info;
                info.EvaVesselId = eva.vessel.id;
                var crew = eva.vessel.GetVesselCrew();
                if (crew != null && crew.Count > 0 && crew[0] != null) info.KerbalName = crew[0].name;
                return info;
            }
        }

        internal static void ReportBoarding(BoardingInfo who, Part target, int seatIndex)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Network == null || !addon.Network.IsConnected || target == null || target.vessel == null) return;
            if (!addon.Vessels.IsOwnedByOther(target.vessel.id)) return;   // ours to do locally
            if (string.IsNullOrEmpty(who.KerbalName))
            {
                Log.Warn("Boarding " + target.vessel.GetDisplayName() + " could not be reported: the kerbal's name was not known before the board");
                return;
            }
            try
            {
                addon.Network.Send(MessageId.CrewBoard, new CrewBoardMsg
                {
                    ToVesselId = target.vessel.id,
                    PartFlightId = target.flightID,
                    SeatIndex = seatIndex,
                    KerbalName = who.KerbalName,
                    EvaVesselId = who.EvaVesselId,
                }, Channel.Control, Delivery.ReliableOrdered);
                Log.Info("Told " + target.vessel.GetDisplayName() + "'s owner that " + who.KerbalName + " is boarding");
            }
            catch (Exception e)
            {
                Log.Exception("Reporting a boarding", e);
            }
        }
    }

    [HarmonyPatch(typeof(KerbalEVA), nameof(KerbalEVA.BoardSeat))]
    internal static class KerbalEVA_BoardSeat
    {
        private static void Prefix(KerbalEVA __instance, out KerbalEVA_ProceedAndBoard.BoardingInfo __state) => __state = KerbalEVA_ProceedAndBoard.BoardingInfo.Of(__instance);

        private static void Postfix(KerbalEVA __instance, KerbalSeat seat, bool __result, KerbalEVA_ProceedAndBoard.BoardingInfo __state)
        {
            if (!__result || seat == null) return;
            // An external command seat is a part module, not a numbered seat in a pod, so there is no index to
            // send: -1 means "the first free one", which for a one-seat part is the only one.
            KerbalEVA_ProceedAndBoard.ReportBoarding(__state, seat.part, seatIndex: -1);
        }
    }
}
