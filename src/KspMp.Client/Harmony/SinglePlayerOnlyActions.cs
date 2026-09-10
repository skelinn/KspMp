using System;
using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>Things that would move one player's time away from everyone else's: pausing and quickloading.</summary>
    internal static class MultiplayerGuard
    {
        public static bool Connected => KspMpAddon.Instance != null && KspMpAddon.Instance.Network != null && KspMpAddon.Instance.Network.IsConnected;

        public static bool Block(string what)
        {
            if (!Connected) return true;
            ScreenMessages.PostScreenMessage(what + " is not available in multiplayer", 3f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Blocked: " + what);
            return false;
        }
    }

    /// <summary>
    /// Reverting is allowed, with two conditions and some tidying.
    ///
    /// Revert to launch puts the game back to the state KSP saved right after the flight began, which still
    /// holds the same vessel under the same id; the next flight-ready snapshot simply overwrites the server's
    /// record of it. Revert to the editor leaves flight altogether, so the vessel is withdrawn. Either way,
    /// everything this player's flight created since launch (spent stages, a kerbal on EVA, a flag) ceases to
    /// exist on this machine without a destroy event, so it is withdrawn from the server first; otherwise the
    /// other player would keep a frozen copy of every stage forever.
    ///
    /// The conditions: only the player simulating the vessel may revert it (a co-pilot reverting would rewind
    /// their copy while the pilot flies on), and not while somebody else is aboard (their machine would be
    /// left flying a vessel that, for the pilot, is back on the pad).
    /// </summary>
    internal static class RevertGuard
    {
        public static bool Allow(string what, bool vesselComesBack)
        {
            if (!MultiplayerGuard.Connected) return true;
            var addon = KspMpAddon.Instance;
            // A revert acts on the vessel this flight launched - KSP restores PostInitState.ActiveVesselID -
            // not on whatever the player has switched to since (a spent stage, say).
            var launched = LaunchedVesselId();
            var label = LabelOf(launched);
            if (launched != Guid.Empty && addon.Vessels.IsOwnedByOther(launched))
                return Refuse(what, "only the pilot of " + label + " can do that");
            // A player who joined somebody else's flight has no launch of their own to go back to. KSP still
            // offers the button (it builds a state from the moment they arrived), and taking it dropped them
            // into a stale cached world - the "launched into the atmosphere" with the colours wrong.
            if (addon.JoinedThisFlight)
                return Refuse(what, "you joined this flight rather than launching it; ask whoever launched to revert");
            Log.Info(what + ": allowed" + (launched != Guid.Empty ? " for " + label : ""));
            // Everyone else aboard loses the craft: it is withdrawn for all of them. Tell them who did it,
            // rather than letting the vessel vanish out from under them with no explanation.
            if (launched != Guid.Empty && addon.Control.OthersAboard(launched))
                Log.Info(what + ": " + label + " has other players aboard; they will be sent to the space centre");
            addon.VesselProto.OnReverting(what, vesselComesBack, launched);
            return true;
        }

        private static Guid LaunchedVesselId()
        {
            var backup = FlightDriver.PostInitState;
            if (backup != null && backup.ActiveVesselID != Guid.Empty) return backup.ActiveVesselID;
            var active = FlightGlobals.ActiveVessel;
            return active != null ? active.id : Guid.Empty;
        }

        private static string LabelOf(Guid id)
        {
            if (id == Guid.Empty) return "the vessel";
            var vessel = FlightGlobals.FindVessel(id);
            return vessel != null ? vessel.GetDisplayName() : "vessel " + id.ToString().Substring(0, 8);
        }

        private static bool Refuse(string what, string why)
        {
            ScreenMessages.PostScreenMessage(what + ": " + why, 4f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Refused " + what + ": " + why);
            return false;
        }
    }

    /// <summary>The pause menu still opens, but time keeps flowing for everyone.</summary>
    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.SetPause), typeof(bool), typeof(bool))]
    internal static class FlightDriver_SetPause
    {
        private static bool Prefix(bool pauseState) => !(pauseState && MultiplayerGuard.Connected);
    }

    [HarmonyPatch(typeof(QuickSaveLoad), "quickLoad", typeof(string), typeof(string))]
    internal static class QuickSaveLoad_QuickLoad
    {
        private static bool Prefix() => MultiplayerGuard.Block("Quickload");
    }

    /// <summary>Recovering or terminating a craft somebody else flies: theirs to do, not ours.</summary>
    internal static class OwnershipGuard
    {
        public static bool AllowRemoval(string what, Vessel vessel)
        {
            if (!MultiplayerGuard.Connected || vessel == null) return true;
            var addon = KspMpAddon.Instance;
            if (addon == null || !addon.Vessels.IsOwnedByOther(vessel.id)) return true;
            var owner = addon.Vessels.OwnerOf(vessel.id);
            var who = addon.Players.TryGet(owner, out var p) ? p.Name : "another player";
            ScreenMessages.PostScreenMessage(what + ": only " + who + ", who flies " + vessel.GetDisplayName() + ", can do that", 4f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Refused " + what + " of " + vessel.GetDisplayName() + ": it is " + who + "'s");
            return false;
        }
    }

    /// <summary>The Recover button in flight (KSP's handler for the recovery request).</summary>
    [HarmonyPatch(typeof(VesselRetrieval), "onVesselRecoveryRequested", typeof(Vessel))]
    internal static class VesselRetrieval_OnVesselRecoveryRequested
    {
        private static bool Prefix(Vessel v) => OwnershipGuard.AllowRemoval("Recover", v);
    }

    /// <summary>Recover in the tracking station, once confirmed.</summary>
    [HarmonyPatch(typeof(KSP.UI.Screens.SpaceTracking), "OnRecoverConfirm")]
    internal static class SpaceTracking_OnRecoverConfirm
    {
        private static bool Prefix(KSP.UI.Screens.SpaceTracking __instance)
        {
            if (OwnershipGuard.AllowRemoval("Recover", __instance.selectedVessel)) return true;
            __instance.OnDialogDismiss();
            return false;
        }
    }

    /// <summary>Terminate in the tracking station, once confirmed.</summary>
    [HarmonyPatch(typeof(KSP.UI.Screens.SpaceTracking), "OnVesselDeleteConfirm")]
    internal static class SpaceTracking_OnVesselDeleteConfirm
    {
        private static bool Prefix(KSP.UI.Screens.SpaceTracking __instance)
        {
            if (OwnershipGuard.AllowRemoval("Terminate", __instance.selectedVessel)) return true;
            __instance.OnDialogDismiss();
            return false;
        }
    }

    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.RevertToLaunch))]
    internal static class FlightDriver_RevertToLaunch
    {
        private static bool Prefix() => RevertGuard.Allow("Revert to launch", vesselComesBack: true);
    }

    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.RevertToPrelaunch), typeof(EditorFacility))]
    internal static class FlightDriver_RevertToPrelaunch
    {
        private static bool Prefix() => RevertGuard.Allow("Revert to the editor", vesselComesBack: false);
    }

    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.ReturnToEditor), typeof(EditorFacility))]
    internal static class FlightDriver_ReturnToEditor
    {
        private static bool Prefix() => RevertGuard.Allow("Return to the editor", vesselComesBack: false);
    }
}
