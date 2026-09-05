using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>
    /// Launching a shared craft tells the other builders, so their workbench is cleared too - and refuses
    /// the launch outright when someone else's craft is still on the pad, which would otherwise spawn the
    /// two inside each other and destroy both with all crew aboard.
    /// </summary>
    [HarmonyPatch(typeof(EditorLogic), nameof(EditorLogic.launchVessel), typeof(string))]
    internal static class EditorLogic_LaunchVessel
    {
        private static bool Prefix(EditorLogic __instance, string siteName)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || !addon.Network.IsConnected) return true;
            if (Vessels.LaunchSiteGuard.IsBlocked(siteName, addon.Vessels, out var reason))
            {
                Log.Info("Refused a launch from the " + siteName + ": " + reason);
                ScreenMessages.PostScreenMessage(reason, 6f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            var ship = __instance != null ? __instance.ship : null;
            addon.AnnounceLaunch(ship != null ? ship.shipName : "a craft", siteName);
            Vessels.LaunchSiteGuard.Clear(siteName);
            return true;
        }
    }
}
