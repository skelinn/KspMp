using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>In-game window with players and chat. Alt+M toggles it; typing in chat locks KSP's keyboard shortcuts.</summary>
    internal sealed class HudWindow
    {
        private const float Width = 380f;
        private const string KeyboardLockId = "KspMp.chat";
        private static readonly int WindowId = "KspMp.Hud".GetHashCode();
        private readonly KspMpAddon _addon;
        private readonly ChatPanel _chat;
        private Rect _rect = new Rect(0, 60, Width, 0);
        private bool _keyboardLocked;
        // The addon is created before KSP has settled on a resolution, so anchoring to the right edge in the
        // field initialiser would put the window wherever the loading screen happened to be wide.
        private bool _placed;

        public bool Visible = true;

        public HudWindow(KspMpAddon addon)
        {
            _addon = addon;
            _chat = new ChatPanel(addon.Chat);
        }

        public void Draw()
        {
            var show = Visible && _addon.Network.IsConnected && HighLogic.LoadedSceneIsGame;
            if (!show)
            {
                SetKeyboardLock(false);
                return;
            }
            if (!_placed)
            {
                _placed = true;
                _rect.x = Theme.ScreenW - Width - 20;
            }
            Theme.Begin();
            _rect = Theme.Clamp(GUILayout.Window(WindowId, _rect, DrawContents,
                                                 "KspMp" + Theme.Tint("   Alt+M", Theme.Dim), GUILayout.Width(Width)));
            Theme.End();
            SetKeyboardLock(_chat.InputFocused);
        }

        private void DrawContents(int id)
        {
            var net = _addon.Network;

            GUILayout.Label(Theme.Dot(Theme.Accent) + "  <b>" + net.ServerName + "</b>", Theme.Value);

            // The numbers that say whether the session is healthy, small and side by side so they can be
            // taken in at a glance rather than read.
            var drift = _addon.TimeSync.DriftSeconds * 1000;
            GUILayout.BeginHorizontal();
            GUILayout.Label(net.PingMs + " ms", Theme.Chip);
            GUILayout.Label(Theme.Tint("drift ", Theme.Dim)
                            + Theme.Tint(drift.ToString("F0") + " ms", System.Math.Abs(drift) > 250 ? Theme.Warn : Theme.Dim), Theme.Chip);
            GUILayout.Label(Theme.Tint("warp ", Theme.Dim) + _addon.Warp.StatusText, Theme.Chip);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Theme.Separator();

            foreach (var p in _addon.Players.Players)
            {
                var you = p.ClientId == net.ClientId;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Theme.Dot(Theme.PlayerColour(p.ClientId)) + "  " + p.Name
                                + (string.IsNullOrEmpty(p.AvatarKerbalName) ? "" : Theme.Tint("  " + p.AvatarKerbalName, Theme.Dim)),
                                Theme.Value);
                GUILayout.FlexibleSpace();
                GUILayout.Label(you ? "you" : p.PingMs + " ms", Theme.Key);
                GUILayout.EndHorizontal();
                GUILayout.Label("      " + _addon.Presence.Describe(p.ClientId), Theme.Caption);
            }

            if (HighLogic.LoadedSceneIsFlight && !string.IsNullOrEmpty(_addon.Control.RoleText))
            {
                Theme.Separator();
                GUILayout.Label(Theme.Tint("Control  ", Theme.Dim) + Theme.Tint(_addon.Control.RoleText, Theme.Warn), Theme.Value);
            }

            GUILayout.Space(4);
            _chat.Draw(140);
            Theme.DragHeader();
        }

        private void SetKeyboardLock(bool locked)
        {
            if (locked == _keyboardLocked) return;
            _keyboardLocked = locked;
            if (locked) InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, KeyboardLockId);
            else InputLockManager.RemoveControlLock(KeyboardLockId);
        }
    }
}
