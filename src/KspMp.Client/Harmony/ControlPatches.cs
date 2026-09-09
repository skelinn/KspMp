using HarmonyLib;
using KSP.UI.Screens;
using KspMp.Systems;

namespace KspMp.Harmony
{
    /// <summary>Decides who gets to act on the active vessel: the owner locally, co-pilots via relay, spectators not at all.</summary>
    internal static class ControlGate
    {
        public enum Verdict { Local, Relay, Blocked }

        public static Verdict For(Vessel vessel)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Network == null || !addon.Network.IsConnected || addon.Control == null || vessel == null) return Verdict.Local;
            if (ControlSystem.ApplyingRemoteAction) return Verdict.Local;
            if (!addon.Vessels.IsKnown(vessel.id) || addon.Vessels.IsMine(vessel.id) || !addon.Vessels.IsOwnedByOther(vessel.id)) return Verdict.Local;
            if (addon.Control.IAmAboard(vessel.id)) return Verdict.Relay;
            return Verdict.Blocked;
        }

        public static void Blocked(string what)
        {
            ScreenMessages.PostScreenMessage("You are not aboard this vessel (" + what + ")", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// We fly this vessel and somebody else is aboard. They see the throttle we stream, but a stage, an
        /// action group or a part button only ever changed our copy: a rocket whose engines never lit, whose
        /// chutes never opened and whose escape tower never left is what a co-pilot had been looking at. So the
        /// action runs here and is also sent, for them to mirror on theirs.
        /// </summary>
        public static bool Echo(Vessel vessel)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || vessel == null || addon.Network == null || !addon.Network.IsConnected || addon.Control == null || addon.Vessels == null) return false;
            // Deliberately not gated on ApplyingRemoteAction: that flag marks a co-pilot's copy mirroring the
            // pilot (where IsMine is false anyway) and the pilot applying a co-pilot's relayed action - and the
            // relayed one must be echoed, because the co-pilot's own copy did nothing but send it, and every
            // other co-pilot has heard nothing at all.
            // Anyone else in flight, not only those aboard: a friend watching from outside (or on EVA beside
            // us) has a loaded copy of this vessel that mirrors the action too.
            return addon.Vessels.IsMine(vessel.id) && (addon.Control.OthersAboard(vessel.id) || (addon.Presence != null && addon.Presence.OthersInFlight()));
        }
    }

    [HarmonyPatch(typeof(StageManager), nameof(StageManager.ActivateStage), typeof(int))]
    internal static class StageManager_ActivateStage
    {
        private static bool Prefix(int stage)
        {
            var vessel = FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                    KspMpAddon.Instance.Control.SendStage(vessel.id);
                    return false;
                case ControlGate.Verdict.Blocked:
                    ControlGate.Blocked("staging");
                    return false;
                default:
                    // The index matters: a co-pilot's copy that fires "whatever is next" by its own count drifts
                    // from the pilot's within a couple of stages, and then decouples what the pilot did not.
                    // ActivateNextStage passes _currentStage - 1, which is -1 on the last stage - the "no index"
                    // sentinel on the wire; KSP clamps it to 0 itself.
                    if (ControlGate.Echo(vessel)) KspMpAddon.Instance.Control.SendStage(vessel.id, quiet: true, stage: stage < 0 ? 0 : stage);
                    return true;
            }
        }
    }

    /// <summary>
    /// SetGroup calls ToggleGroup underneath, and the navball button calls SetGroup: one click would be
    /// echoed by each patch on the way down. Whichever patch is outermost echoes; the ones inside stand aside.
    /// </summary>
    internal static class ActionGroupNesting
    {
        public static int Depth;
    }

    [HarmonyPatch(typeof(ActionGroupList), nameof(ActionGroupList.ToggleGroup), typeof(KSPActionGroup))]
    internal static class ActionGroupList_ToggleGroup
    {
        private static bool Prefix(ActionGroupList __instance, KSPActionGroup group)
        {
            if (ActionGroupNesting.Depth > 0) return true;
            var vessel = __instance.v;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                    // The state it should end in, not a blind toggle: a toggle lost to the group's cooldown on
                    // the other side inverted the gear for the rest of the flight.
                    KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, false, !__instance[group]);
                    return false;
                case ControlGate.Verdict.Blocked:
                    ControlGate.Blocked(group.ToString());
                    return false;
                default:
                    if (ControlGate.Echo(vessel)) KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, false, !__instance[group]);
                    return true;
            }
        }
    }

    /// <summary>
    /// The gear / lights / brakes / SAS / RCS / abort buttons beside the navball. They call SetGroup, not
    /// ToggleGroup, so the ToggleGroup patch never saw a click on them: a co-pilot's click changed their
    /// copy only, and the pilot's click was never echoed.
    /// </summary>
    [HarmonyPatch(typeof(KSP.UI.Screens.Flight.ActionGroupToggleButton), "SetToggle")]
    internal static class ActionGroupToggleButton_SetToggle
    {
        private static bool Prefix(KSP.UI.Screens.Flight.ActionGroupToggleButton __instance, ref bool __state)
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return true;
            var group = __instance.group;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                    KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, false, !vessel.ActionGroups[group]);
                    return false;
                case ControlGate.Verdict.Blocked:
                    ControlGate.Blocked(group.ToString());
                    return false;
                default:
                    if (ControlGate.Echo(vessel)) KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, false, !vessel.ActionGroups[group]);
                    ActionGroupNesting.Depth++;
                    __state = true;
                    return true;
            }
        }

        private static void Postfix(bool __state) { if (__state) ActionGroupNesting.Depth--; }
    }

    /// <summary>
    /// The brakes key sets the group rather than toggling it (down = on, up = off), so it went past the
    /// ToggleGroup patch too. Brakes only: SetGroup is also how the autopilot drops SAS it cannot hold and
    /// how contracts arm a spawned craft, and those are not a player's doing.
    /// </summary>
    [HarmonyPatch(typeof(ActionGroupList), nameof(ActionGroupList.SetGroup), typeof(KSPActionGroup), typeof(bool))]
    internal static class ActionGroupList_SetGroup
    {
        private static bool Prefix(ActionGroupList __instance, KSPActionGroup group, bool active, ref bool __state)
        {
            if (ActionGroupNesting.Depth > 0) return true;
            if (group != KSPActionGroup.Brakes) return true;
            var vessel = __instance.v;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                    KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, false, active);
                    return false;
                case ControlGate.Verdict.Blocked:
                    return false;
                default:
                    if (ControlGate.Echo(vessel) && vessel.ActionGroups[group] != active) KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, false, active);
                    ActionGroupNesting.Depth++;
                    __state = true;
                    return true;
            }
        }

        private static void Postfix(bool __state) { if (__state) ActionGroupNesting.Depth--; }
    }

    [HarmonyPatch(typeof(VesselAutopilot), nameof(VesselAutopilot.SetMode), typeof(VesselAutopilot.AutopilotMode))]
    internal static class VesselAutopilot_SetMode
    {
        private static bool Prefix(VesselAutopilot __instance, VesselAutopilot.AutopilotMode mode, ref bool __result)
        {
            var vessel = __instance.Vessel;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                    KspMpAddon.Instance.Control.SendSasMode(vessel.id, (int)mode, true);
                    __result = true;   // SetMode returns whether the mode was taken; the SAS buttons stay stuck otherwise
                    return false;
                case ControlGate.Verdict.Blocked:
                    __result = false;
                    return false;
                default:
                    if (ControlGate.Echo(vessel)) KspMpAddon.Instance.Control.SendSasMode(vessel.id, (int)mode, true);
                    return true;
            }
        }
    }

    [HarmonyPatch(typeof(UIPartActionButton), nameof(UIPartActionButton.OnClick))]
    internal static class UIPartActionButton_OnClick
    {
        private static bool Prefix(UIPartActionButton __instance)
        {
            var part = __instance.part;
            var vessel = part != null ? part.vessel : null;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                {
                    var module = __instance.partModule;
                    var index = module != null ? part.Modules.IndexOf(module) : -1;
                    var evt = __instance.evt;
                    if (evt == null) return false;
                    KspMpAddon.Instance.Control.SendPartEvent(vessel.id, part.flightID, index, evt.name);
                    // KSP's own click runs the event on every symmetry counterpart too (UIPartActionButton.OnClick).
                    if (part.symmetryCounterparts != null)
                        foreach (var twin in part.symmetryCounterparts)
                            if (twin != null) KspMpAddon.Instance.Control.SendPartEvent(vessel.id, twin.flightID, index, evt.name, quiet: true);
                    return false;
                }
                case ControlGate.Verdict.Blocked:
                    ControlGate.Blocked("part action");
                    return false;
                default:
                    if (ControlGate.Echo(vessel))
                    {
                        var module = __instance.partModule;
                        var index = module != null ? part.Modules.IndexOf(module) : -1;
                        var evt = __instance.evt;
                        if (evt != null)
                        {
                            KspMpAddon.Instance.Control.SendPartEvent(vessel.id, part.flightID, index, evt.name, quiet: true);
                            if (part.symmetryCounterparts != null)
                                foreach (var twin in part.symmetryCounterparts)
                                    if (twin != null) KspMpAddon.Instance.Control.SendPartEvent(vessel.id, twin.flightID, index, evt.name, quiet: true);
                        }
                    }
                    return true;
            }
        }
    }
}
