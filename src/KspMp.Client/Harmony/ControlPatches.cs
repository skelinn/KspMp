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
            if (addon == null || vessel == null || addon.Network == null || !addon.Network.IsConnected || addon.Control == null) return false;
            if (ControlSystem.ApplyingRemoteAction) return false;
            return addon.Vessels.IsMine(vessel.id) && addon.Control.OthersAboard(vessel.id);
        }
    }

    [HarmonyPatch(typeof(StageManager), nameof(StageManager.ActivateStage), typeof(int))]
    internal static class StageManager_ActivateStage
    {
        private static bool Prefix()
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
                    if (ControlGate.Echo(vessel)) KspMpAddon.Instance.Control.SendStage(vessel.id, quiet: true);
                    return true;
            }
        }
    }

    [HarmonyPatch(typeof(ActionGroupList), nameof(ActionGroupList.ToggleGroup), typeof(KSPActionGroup))]
    internal static class ActionGroupList_ToggleGroup
    {
        private static bool Prefix(ActionGroupList __instance, KSPActionGroup group)
        {
            var vessel = __instance.v;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                    KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, true, false);
                    return false;
                case ControlGate.Verdict.Blocked:
                    ControlGate.Blocked(group.ToString());
                    return false;
                default:
                    if (ControlGate.Echo(vessel)) KspMpAddon.Instance.Control.SendActionGroup(vessel.id, group, true, false);
                    return true;
            }
        }
    }

    [HarmonyPatch(typeof(VesselAutopilot), nameof(VesselAutopilot.SetMode), typeof(VesselAutopilot.AutopilotMode))]
    internal static class VesselAutopilot_SetMode
    {
        private static bool Prefix(VesselAutopilot __instance, VesselAutopilot.AutopilotMode mode)
        {
            var vessel = __instance.Vessel;
            switch (ControlGate.For(vessel))
            {
                case ControlGate.Verdict.Relay:
                    KspMpAddon.Instance.Control.SendSasMode(vessel.id, (int)mode, true);
                    return false;
                case ControlGate.Verdict.Blocked:
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
                        if (evt != null) KspMpAddon.Instance.Control.SendPartEvent(vessel.id, part.flightID, index, evt.name, quiet: true);
                    }
                    return true;
            }
        }
    }
}
