using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>
    /// Names floating over other players' kerbals and craft.
    ///
    /// Without them a multiplayer flight is a stranger's rocket doing something inexplicable a kilometre away,
    /// and an EVA is two identical white suits. Each player keeps the colour they have everywhere else in the
    /// mod, so the dot beside a name in the HUD and the tag over their kerbal are recognisably the same person.
    ///
    /// Drawn outside <see cref="Theme.Begin"/>: the theme scales the whole GUI matrix for window layout, and
    /// these coordinates come from the camera in real screen pixels.
    /// </summary>
    internal static class NametagOverlay
    {
        /// <summary>Beyond this a tag is noise; EVA kerbals are small enough to want a shorter leash.</summary>
        public const float VesselRangeMeters = 5000f;
        public const float EvaRangeMeters = 1000f;

        private static GUIStyle _style;

        public static void Draw(KspMpAddon addon)
        {
            if (addon == null || addon.Settings == null || !addon.Settings.ShowNametags) return;
            if (!addon.Network.IsConnected || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            if (MapView.MapIsEnabled) return;
            var camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (camera == null) return;

            Theme.Ensure();
            if (_style == null)
                _style = new GUIStyle(Theme.Chip) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleCenter };

            var active = FlightGlobals.ActiveVessel;
            var showAll = addon.Launch != null && addon.Launch.NametagsIncludeOwnVessel;
            var vessels = FlightGlobals.Vessels;
            for (var i = 0; i < vessels.Count; i++)
            {
                var vessel = vessels[i];
                if (vessel == null || !vessel.loaded) continue;
                if (!showAll && vessel == active) continue;
                switch (vessel.vesselType)
                {
                    case VesselType.Debris:
                    case VesselType.SpaceObject:
                    case VesselType.Unknown:
                    case VesselType.Flag:
                        continue;
                }

                Vector3d world;
                string text;
                if (vessel.isEVA)
                {
                    if (!TryDescribeKerbal(addon, vessel, out text)) continue;
                    var eva = vessel.evaController;
                    if (eva == null) continue;
                    world = eva.transform.position + eva.transform.up * 0.7f;
                    if ((world - FlightGlobals.ActiveVessel.GetWorldPos3D()).magnitude > EvaRangeMeters) continue;
                }
                else
                {
                    if (!TryDescribeVessel(addon, vessel, out text)) continue;
                    world = vessel.GetWorldPos3D() + vessel.transform.up * (vessel.vesselSize.y / 2f + 2f);
                    if (active != null && (world - active.GetWorldPos3D()).magnitude > VesselRangeMeters) continue;
                }

                var screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0) continue;   // behind the camera, where it would be drawn mirrored
                var size = _style.CalcSize(new GUIContent(text));
                GUI.Label(new Rect(screen.x - size.x / 2f, Screen.height - screen.y - size.y, size.x, size.y), text, _style);
            }
        }

        /// <summary>A kerbal on EVA is tagged with the player it belongs to, not just its own name.</summary>
        private static bool TryDescribeKerbal(KspMpAddon addon, Vessel vessel, out string text)
        {
            text = null;
            var crew = vessel.GetVesselCrew();
            if (crew == null || crew.Count == 0 || crew[0] == null) return false;
            var kerbal = crew[0].name;
            var clientId = 0;
            if (addon.Roster != null && addon.Roster.TryGet(kerbal, out var remote) && remote.IsAvatar) clientId = remote.AvatarClientId;
            if (clientId == 0) clientId = addon.Vessels.OwnerOf(vessel.id);
            if (clientId == 0) return false;
            text = Theme.Tint(NameOf(addon, clientId), Theme.PlayerColour(clientId)) + Theme.Tint("  " + kerbal, Theme.Dim);
            return true;
        }

        private static bool TryDescribeVessel(KspMpAddon addon, Vessel vessel, out string text)
        {
            text = null;
            var pilot = addon.Control != null ? addon.Control.PilotOf(vessel.id) : 0;
            var owner = addon.Vessels.OwnerOf(vessel.id);
            var colourFor = pilot != 0 ? pilot : owner;
            if (colourFor == 0) return false;   // nobody's: stock debris and scenery stay untagged

            text = Theme.Tint(vessel.GetDisplayName(), colourFor == 0 ? Theme.Dim : Theme.PlayerColour(colourFor));
            if (pilot != 0) text += Theme.Tint("  flown by " + NameOf(addon, pilot), Theme.Ink);
            else if (owner != 0) text += Theme.Tint("  simulated by " + NameOf(addon, owner), Theme.Dim);
            if (addon.Control != null && addon.Control.TryGetRoles(vessel.id, out var roles) && roles.AboardClientIds != null && roles.AboardClientIds.Length > 1)
                text += Theme.Tint("  · " + roles.AboardClientIds.Length + " aboard", Theme.Dim);
            return true;
        }

        private static string NameOf(KspMpAddon addon, int clientId) =>
            addon.Players != null && addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;
    }
}
