using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>Launching a shared craft tells the other builders, so their workbench is cleared too.</summary>
    [HarmonyPatch(typeof(EditorLogic), nameof(EditorLogic.launchVessel), typeof(string))]
    internal static class EditorLogic_LaunchVessel
    {
        private static void Prefix(EditorLogic __instance, string siteName)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Editor == null || !addon.Editor.Active) return;
            var ship = __instance != null ? __instance.ship : null;
            addon.Editor.AnnounceLaunch(ship != null ? ship.shipName : "a craft", siteName);
        }
    }
}
