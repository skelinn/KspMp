using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>Status window for working out what the mod is doing. Alt+F10 toggles it.</summary>
    internal sealed class DebugWindow
    {
        private const float Width = 470f;
        private static readonly int WindowId = "KspMp.DebugWindow".GetHashCode();
        private readonly KspMpAddon _addon;
        private Rect _rect = new Rect(20, 20, Width, 0);

        public bool Visible;

        public DebugWindow(KspMpAddon addon)
        {
            _addon = addon;
        }

        public void Draw()
        {
            if (!Visible) return;
            Theme.Begin();
            _rect = Theme.Clamp(GUILayout.Window(WindowId, _rect, DrawContents,
                                     "KspMp debug" + Theme.Tint("   " + KspMpAddon.Version, Theme.Dim), GUILayout.Width(Width)));
            Theme.End();
        }

        private void DrawContents(int id)
        {
            var net = _addon.Network;
            var time = _addon.TimeSync;
            var patched = Harmony.HarmonyBootstrap.Patched;

            Theme.BeginSection("SESSION");
            Theme.Row("Scene", HighLogic.LoadedScene + Theme.Tint("   save " + (HighLogic.CurrentGame != null ? HighLogic.SaveFolder : "-"), Theme.Dim));
            Theme.Row("Harmony", patched
                ? Theme.Tint(Harmony.HarmonyBootstrap.PatchCount + " method(s) patched", Theme.Accent)
                : Theme.Tint("NOT PATCHED", Theme.Bad));
            Theme.Row("Player", _addon.Settings.PlayerName
                                + Theme.Tint("   " + _addon.Settings.PlayerId.ToString().Substring(0, 8), Theme.Dim));
            Theme.EndSection();

            Theme.BeginSection("NETWORK");
            Theme.Row("State", Theme.Tint(net.State.ToString(), net.IsConnected ? Theme.Accent : Theme.Warn) + "   " + net.Status);
            Theme.Row("Ping", net.PingMs + " ms" + Theme.Tint("   rtt sample " + time.RttMs.ToString("F0") + " ms", Theme.Dim));
            Theme.EndSection();

            Theme.BeginSection("TIME");
            Theme.Row("Server UT", (time.HasSync ? time.ServerUt.ToString("F2") : "-")
                                   + Theme.Tint("   local " + (Planetarium.fetch != null ? Planetarium.GetUniversalTime().ToString("F2") : "-"), Theme.Dim));
            var drift = time.DriftSeconds * 1000;
            Theme.Row("Drift", Theme.Tint(drift.ToString("F0") + " ms", System.Math.Abs(drift) > 250 ? Theme.Warn : Theme.Accent)
                               + Theme.Tint("   " + time.Corrections + " correction(s), rate " + time.Rate + "x", Theme.Dim));
            Theme.Row("Warp", _addon.Warp.StatusText + Theme.Tint("   timeScale " + Time.timeScale.ToString("F2"), Theme.Dim));
            Theme.EndSection();

            Theme.BeginSection("WORLD");
            Theme.Row("Players", _addon.Players.Count + Theme.Tint("   " + _addon.Chat.Lines.Count + " chat line(s)", Theme.Dim));
            Theme.Row("Roster", _addon.Roster.Count + " kerbal(s), avatar "
                                + (_addon.Roster.HasAvatar ? _addon.Roster.AvatarName : Theme.Tint("none", Theme.Dim)));
            Theme.Row("Presence", _addon.Presence.Describe(net.ClientId));
            Theme.Row("Vessels", _addon.Vessels.Count + " known, " + _addon.Vessels.CountOwnedByMe + " ours, "
                                 + _addon.Vessels.CountReplicas + " replica(s)");
            Theme.Row("Protos", _addon.VesselProto.Sent + " sent / " + _addon.VesselProto.Applied + " applied");
            Theme.Row("States", _addon.VesselState.Sent + " sent / " + _addon.VesselState.Received + " received");
            if (_addon.Editor.Active)
                Theme.Row("Editor", "revision " + _addon.Editor.Revision + ", " + _addon.Editor.BuilderCount + " builder(s), "
                                    + _addon.Editor.SnapshotsSent + " sent / " + _addon.Editor.SnapshotsApplied + " applied");
            Theme.EndSection();

            Theme.BeginSection("CONTROL");
            Theme.Row("Role", _addon.Control.RoleText);
            Theme.Row("Inputs", _addon.Control.InputsSent + " sent / " + _addon.Control.InputsReceived + " received");
            Theme.Row("Actions", _addon.Control.ActionsApplied + " applied");
            Theme.EndSection();

            if (GUILayout.Button("Hide" + Theme.Tint("   Alt+F10", Theme.Dim))) Visible = false;
            Theme.DragHeader();
        }
    }
}
