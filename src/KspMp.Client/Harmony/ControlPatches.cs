using System;
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
        /// <summary>Set while KSP's own autopilot is doing its per-frame housekeeping, which is nobody's decision.</summary>
        public static bool InsideAutopilot;
    }

    /// <summary>
    /// Marks KSP's own autopilot housekeeping. VesselAutopilot.Update puts the mode back to Stability Assist
    /// and drops the SAS action group whenever it cannot hold the mode it is in, every frame, for as long as
    /// that is true - which on a copy of somebody else's vessel is forever. None of that is a player acting.
    /// </summary>
    [HarmonyPatch(typeof(VesselAutopilot), nameof(VesselAutopilot.Update))]
    internal static class VesselAutopilot_Update
    {
        private static void Prefix(out bool __state)
        {
            __state = ActionGroupNesting.InsideAutopilot;
            ActionGroupNesting.InsideAutopilot = true;
        }

        private static Exception Finalizer(bool __state)
        {
            ActionGroupNesting.InsideAutopilot = __state;
            return null;
        }
    }

    [HarmonyPatch(typeof(ActionGroupList), nameof(ActionGroupList.ToggleGroup), typeof(KSPActionGroup))]
    internal static class ActionGroupList_ToggleGroup
    {
        private static bool Prefix(ActionGroupList __instance, KSPActionGroup group)
        {
            if (ActionGroupNesting.Depth > 0 || ActionGroupNesting.InsideAutopilot) return true;
            // The space bar fires the stage and then toggles the Stage group (FlightInputHandler), so relaying
            // both put two messages on the wire for one press and fired the group's actions a second time on
            // the other copy, untied from whether the stage itself went off. Staging travels as a stage.
            if (group == KSPActionGroup.Stage) return true;
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

        // A finalizer, not a postfix: Harmony only wraps the original in a try/catch when one exists, and a
        // part action that throws would otherwise leave the counter up for good - after which every gear,
        // light and brake press silently stopped crossing the wire for the rest of the session.
        private static Exception Finalizer(bool __state) { if (__state) ActionGroupNesting.Depth--; return null; }
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
            if (ActionGroupNesting.Depth > 0 || ActionGroupNesting.InsideAutopilot) return true;
            if (group != KSPActionGroup.Brakes)
            {
                // Not relayed here, but SetGroup calls ToggleGroup underneath, and without marking the nesting
                // that inner call reads as a player toggling the group. KSP's autopilot dropping SAS it cannot
                // hold went out as the player's doing, every frame.
                ActionGroupNesting.Depth++;
                __state = true;
                return true;
            }
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

        private static Exception Finalizer(bool __state) { if (__state) ActionGroupNesting.Depth--; return null; }
    }

    [HarmonyPatch(typeof(VesselAutopilot), nameof(VesselAutopilot.SetMode), typeof(VesselAutopilot.AutopilotMode))]
    internal static class VesselAutopilot_SetMode
    {
        private static bool Prefix(VesselAutopilot __instance, VesselAutopilot.AutopilotMode mode, ref bool __result)
        {
            var vessel = __instance.Vessel;
            // KSP's own autopilot puts itself back to Stability Assist every frame it cannot hold the mode it
            // is in. On a copy of somebody else's vessel that is permanent - a maneuver node held on their
            // machine does not exist on ours - so relaying it sent a SAS message sixty times a second and
            // dragged the pilot off their own hold. That housekeeping is not the player changing the mode.
            if (ActionGroupNesting.InsideAutopilot) return true;
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

    /// <summary>
    /// A part-menu slider, toggle or cycle (thrust limiter, fuel flow, ...). They all write through
    /// SetFieldValue. A co-pilot's change is applied to their copy at once and sent to the pilot, whose
    /// copy is the one that matters; the pilot's change is echoed so the co-pilot's copy follows without
    /// waiting for the next snapshot.
    /// </summary>
    [HarmonyPatch(typeof(UIPartActionFieldItem), "SetFieldValue", typeof(object))]
    internal static class UIPartActionFieldItem_SetFieldValue
    {
        private static bool Prefix(UIPartActionFieldItem __instance, object newValue)
        {
            var part = __instance.part;
            var field = __instance.field;
            var vessel = part != null ? part.vessel : null;
            if (field == null) return true;
            var verdict = ControlGate.For(vessel);
            if (verdict == ControlGate.Verdict.Blocked) { ControlGate.Blocked(field.guiName); return false; }
            if (verdict == ControlGate.Verdict.Relay || ControlGate.Echo(vessel))
            {
                // Nothing to say when the value is not changing: KSP makes the same test before it writes.
                object current = null;
                try { current = field.GetValue(field.host); } catch { }
                if (current != null && Equals(current, newValue)) return true;
                var module = __instance.partModule;
                var index = module != null ? part.Modules.IndexOf(module) : -1;
                // A slider drag calls this on every step it crosses; the last one within a tenth of a second
                // is what goes out, so pulling a thrust limiter from nothing to full sends a handful of
                // messages rather than two hundred reliable ones.
                KspMpAddon.Instance.Control.QueuePartField(vessel.id, part.flightID, index, field.name, ControlSystem.FieldValueText(newValue));
            }
            return true;
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
