using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>In-game window with players and chat. Alt+M toggles it; typing in chat locks KSP's keyboard shortcuts.</summary>
    internal sealed class HudWindow
    {
        private const string KeyboardLockId = "KspMp.chat";
        private static readonly int WindowId = "KspMp.Hud".GetHashCode();
        private readonly KspMpAddon _addon;
        private readonly ChatPanel _chat;
        private Rect _rect = new Rect(Screen.width - 380, 60, 360, 0);
        private bool _keyboardLocked;

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
            GUI.skin = HighLogic.Skin;
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, "KspMp  (Alt+M)", GUILayout.Width(360));
            SetKeyboardLock(_chat.InputFocused);
        }

        private void DrawContents(int id)
        {
            var net = _addon.Network;
            GUILayout.Label("<b>" + net.ServerName + "</b>  " + net.PingMs + " ms  UT drift " + (_addon.TimeSync.DriftSeconds * 1000).ToString("F0") + " ms  warp " + _addon.Warp.StatusText);
            foreach (var p in _addon.Players.Players)
                GUILayout.Label("  " + p.Name + (p.ClientId == net.ClientId ? "  (you)" : "  " + p.PingMs + " ms"));
            if (_addon.Authority.Spectating) GUILayout.Label("<color=#ffd966>Spectating " + _addon.Authority.SpectatingOwnerName + "'s vessel</color>");
            GUILayout.Space(4);
            _chat.Draw(140);
            GUI.DragWindow();
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
