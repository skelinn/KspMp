using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>Things that would move one player's time away from everyone else's: pausing, quickloading, reverting.</summary>
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
        private static bool Prefix() => MultiplayerGuard.Block("Revert to launch");
    }

    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.RevertToPrelaunch), typeof(EditorFacility))]
    internal static class FlightDriver_RevertToPrelaunch
    {
        private static bool Prefix() => MultiplayerGuard.Block("Revert to the editor");
    }

    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.ReturnToEditor), typeof(EditorFacility))]
    internal static class FlightDriver_ReturnToEditor
    {
        private static bool Prefix() => MultiplayerGuard.Block("Return to the editor");
    }
}
