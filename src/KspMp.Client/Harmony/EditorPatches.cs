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
            if (addon == null || addon.Network == null || !addon.Network.IsConnected) return true;
            if (Vessels.LaunchSiteGuard.IsBlocked(siteName, addon.Vessels, out var reason))
            {
                Log.Info("Refused a launch from the " + siteName + ": " + reason);
                ScreenMessages.PostScreenMessage(reason, 6f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            // The announcement waits for FlightDriver.StartWithNewLaunch (see FlightDriver_StartWithNewLaunch):
            // KSP's pre-flight checks run after this and the player may cancel, and then nobody launched.
            LastEditorLaunchCrew = SeatedKerbals();
            LastEditorLaunchCrewAt = UnityEngine.Time.realtimeSinceStartup;
            return true;
        }

        internal static string[] LastEditorLaunchCrew;
        internal static float LastEditorLaunchCrewAt = -1000f;

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

    /// <summary>
    /// Every launch, from the editor or the space centre's own craft browser, ends up here after KSP's
    /// pre-flight checks. This is where the pad is checked once more and the launch is announced: the editor
    /// prefix above checks early for a clear message, but only a launch that is actually happening is
    /// announced, and the space-centre path never went through the editor at all.
    /// </summary>
    [HarmonyPatch(typeof(FlightDriver), nameof(FlightDriver.StartWithNewLaunch), typeof(string), typeof(string), typeof(string), typeof(VesselCrewManifest))]
    internal static class FlightDriver_StartWithNewLaunch
    {
        private static bool Prefix(string fullFilePath, string launchSiteName, VesselCrewManifest manifest)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Network == null || !addon.Network.IsConnected) return true;
            addon.JoinedThisFlight = false;   // our own launch: reverting has somewhere to go back to
            if (addon.SuppressLaunchAnnounce) return true;   // the harness announced and cleared the pad itself
            if (Vessels.LaunchSiteGuard.IsBlocked(launchSiteName, addon.Vessels, out var reason))
            {
                Log.Info("Refused a launch from the " + launchSiteName + ": " + reason);
                ScreenMessages.PostScreenMessage(reason, 6f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            var crew = new List<string>();
            try
            {
                if (manifest != null)
                    foreach (var pcm in manifest.GetAllCrew(false))
                        if (pcm != null && !string.IsNullOrEmpty(pcm.name) && !crew.Contains(pcm.name)) crew.Add(pcm.name);
            }
            catch (System.Exception e)
            {
                Log.Exception("Reading the launch manifest", e);
            }
            // The editor's crew list is only good for the launch it was read for; one cancelled at the
            // pre-flight dialog must not name its crew on the next launch from the space centre.
            var fresh = UnityEngine.Time.realtimeSinceStartup - EditorLogic_LaunchVessel.LastEditorLaunchCrewAt < 30f;
            if (crew.Count == 0 && fresh && EditorLogic_LaunchVessel.LastEditorLaunchCrew != null) crew.AddRange(EditorLogic_LaunchVessel.LastEditorLaunchCrew);
            EditorLogic_LaunchVessel.LastEditorLaunchCrew = null;
            var name = System.IO.Path.GetFileNameWithoutExtension(fullFilePath ?? "") ;
            addon.AnnounceLaunch(string.IsNullOrEmpty(name) ? "a craft" : name, launchSiteName, crew.ToArray());
            Vessels.LaunchSiteGuard.Clear(launchSiteName);
            return true;
        }
    }
}
