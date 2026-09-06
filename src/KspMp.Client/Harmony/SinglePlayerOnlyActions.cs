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
            var active = FlightGlobals.ActiveVessel;
            if (active != null && addon.Vessels.IsOwnedByOther(active.id))
                return Refuse(what, "only the pilot of " + active.GetDisplayName() + " can do that");
            if (active != null && addon.Control.OthersAboard(active.id))
                return Refuse(what, "somebody else is still aboard " + active.GetDisplayName());
            Log.Info(what + ": allowed" + (active != null ? " for " + active.GetDisplayName() : ""));
            addon.VesselProto.OnReverting(what, vesselComesBack);
            return true;
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
