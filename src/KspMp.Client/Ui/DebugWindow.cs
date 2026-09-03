using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>Small IMGUI status window. Alt+F10 toggles it.</summary>
    internal sealed class DebugWindow
    {
        private static readonly int WindowId = "KspMp.DebugWindow".GetHashCode();
        private readonly KspMpAddon _addon;
        private Rect _rect = new Rect(20, 80, 440, 0);

        public bool Visible;

        public DebugWindow(KspMpAddon addon)
        {
            _addon = addon;
        }

        public void Draw()
        {
            if (!Visible) return;
            GUI.skin = HighLogic.Skin;
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, "KspMp " + KspMpAddon.Version, GUILayout.Width(440));
        }

        private void DrawContents(int id)
        {
            var spike = _addon.Spike;
            GUILayout.Label("Scene: " + HighLogic.LoadedScene);
            GUILayout.Label("Harmony: " + (Harmony.HarmonyBootstrap.Patched ? Harmony.HarmonyBootstrap.PatchCount + " method(s) patched" : "NOT PATCHED"));
            GUILayout.Label("Deflate round trip: " + (spike != null ? spike.DeflateStatus : "-"));
            GUILayout.Label("Loopback network: " + (spike != null ? spike.NetStatus : "-"));
            GUILayout.Label("Player: " + _addon.Settings.PlayerName + " (" + _addon.Settings.PlayerId.ToString().Substring(0, 8) + ")");
            if (GUILayout.Button("Hide (Alt+F10 toggles)")) Visible = false;
            GUI.DragWindow();
        }
    }
}
