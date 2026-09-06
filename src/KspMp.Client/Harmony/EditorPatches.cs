using System.Collections.Generic;
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
            addon.AnnounceLaunch(ship != null ? ship.shipName : "a craft", siteName, SeatedKerbals());
            Vessels.LaunchSiteGuard.Clear(siteName);
            return true;
        }

        /// <summary>
        /// Who is in the seats at the moment of launch. The other player only learns their kerbal is going up
        /// from this list, so it is read from the crew dialog's live manifest rather than from the craft file.
        /// </summary>
        private static string[] SeatedKerbals()
        {
            var names = new List<string>();
            try
            {
                var manifest = KSP.UI.CrewAssignmentDialog.Instance != null ? KSP.UI.CrewAssignmentDialog.Instance.GetManifest() : null;
                if (manifest != null)
                    foreach (var crew in manifest.GetAllCrew(false))
                        if (crew != null && !string.IsNullOrEmpty(crew.name) && !names.Contains(crew.name)) names.Add(crew.name);
            }
            catch (System.Exception e)
            {
                Log.Exception("Reading the crew manifest at launch", e);
            }
            Log.Info("Launching with " + (names.Count == 0 ? "nobody" : string.Join(", ", names.ToArray())) + " aboard");
            return names.ToArray();
        }
    }
}
