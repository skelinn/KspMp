using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>Small IMGUI status window. Alt+F10 toggles it.</summary>
    internal sealed class DebugWindow
    {
        private static readonly int WindowId = "KspMp.DebugWindow".GetHashCode();
        private readonly KspMpAddon _addon;
        private Rect _rect = new Rect(20, 20, 440, 0);

        public bool Visible;

        public DebugWindow(KspMpAddon addon)
        {
            _addon = addon;
        }

        public void Draw()
        {
            if (!Visible) return;
            GUI.skin = HighLogic.Skin;
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, "KspMp debug " + KspMpAddon.Version, GUILayout.Width(440));
        }

        private void DrawContents(int id)
        {
            var net = _addon.Network;
            var time = _addon.TimeSync;
            GUILayout.Label("Scene: " + HighLogic.LoadedScene + "   save: " + (HighLogic.CurrentGame != null ? HighLogic.SaveFolder : "-"));
            GUILayout.Label("Harmony: " + (Harmony.HarmonyBootstrap.Patched ? Harmony.HarmonyBootstrap.PatchCount + " method(s) patched" : "NOT PATCHED"));
            GUILayout.Label("Network: " + net.State + "  " + net.Status);
            GUILayout.Label("Ping: " + net.PingMs + " ms   rtt sample: " + time.RttMs.ToString("F0") + " ms");
            GUILayout.Label("Server UT: " + (time.HasSync ? time.ServerUt.ToString("F2") : "-") + "   local UT: " + (Planetarium.fetch != null ? Planetarium.GetUniversalTime().ToString("F2") : "-"));
            GUILayout.Label("UT drift: " + (time.DriftSeconds * 1000).ToString("F0") + " ms   corrections: " + time.Corrections + "   rate: " + time.Rate + "x   warp: " + _addon.Warp.StatusText + "   timeScale " + Time.timeScale.ToString("F2"));
            GUILayout.Label("Players: " + _addon.Players.Count + "   chat lines: " + _addon.Chat.Lines.Count + "   roster: " + _addon.Roster.Count + " kerbal(s), avatar " + (_addon.Roster.HasAvatar ? _addon.Roster.AvatarName : "none") + ", " + _addon.Presence.Describe(net.ClientId));
            GUILayout.Label("Vessels: " + _addon.Vessels.Count + " known, " + _addon.Vessels.CountOwnedByMe + " ours, " + _addon.Vessels.CountReplicas + " replicas"
                            + "   protos sent/applied " + _addon.VesselProto.Sent + "/" + _addon.VesselProto.Applied + "   states sent/recv " + _addon.VesselState.Sent + "/" + _addon.VesselState.Received);
            if (_addon.Editor.Active) GUILayout.Label("Shared editor: revision " + _addon.Editor.Revision + ", " + _addon.Editor.BuilderCount + " builder(s), sent/applied " + _addon.Editor.SnapshotsSent + "/" + _addon.Editor.SnapshotsApplied);
            GUILayout.Label("Control: " + _addon.Control.RoleText + "   inputs sent/recv " + _addon.Control.InputsSent + "/" + _addon.Control.InputsReceived + "   actions applied " + _addon.Control.ActionsApplied);
            GUILayout.Label("Player: " + _addon.Settings.PlayerName + " (" + _addon.Settings.PlayerId.ToString().Substring(0, 8) + ")");
            if (GUILayout.Button("Hide (Alt+F10 toggles)")) Visible = false;
            GUI.DragWindow();
        }
    }
}
