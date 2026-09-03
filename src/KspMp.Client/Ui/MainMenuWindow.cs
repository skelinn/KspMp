using KspMp.Game;
using KspMp.Net;
using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>Main-menu window: connect to a server, then the lobby (players, chat, Enter game).</summary>
    internal sealed class MainMenuWindow
    {
        private static readonly int WindowId = "KspMp.MainMenu".GetHashCode();
        private readonly KspMpAddon _addon;
        private readonly ChatPanel _chat;
        private Rect _rect = new Rect(40, 130, 460, 0);
        private string _port;

        public MainMenuWindow(KspMpAddon addon)
        {
            _addon = addon;
            _chat = new ChatPanel(addon.Chat);
            _port = addon.Settings.Port.ToString();
        }

        public void Draw()
        {
            if (HighLogic.LoadedScene != GameScenes.MAINMENU) return;
            GUI.skin = HighLogic.Skin;
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, "KspMp multiplayer " + KspMpAddon.Version, GUILayout.Width(460));
        }

        private void DrawContents(int id)
        {
            var net = _addon.Network;
            var settings = _addon.Settings;

            if (net.State == ConnectionState.Disconnected)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name", GUILayout.Width(60));
                settings.PlayerName = GUILayout.TextField(settings.PlayerName, 24);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Server", GUILayout.Width(60));
                settings.LastServer = GUILayout.TextField(settings.LastServer);
                GUILayout.Label("Port", GUILayout.Width(40));
                _port = GUILayout.TextField(_port, 5, GUILayout.Width(70));
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Connect"))
                {
                    if (int.TryParse(_port, out var port) && port > 0 && port < 65536)
                    {
                        settings.Port = port;
                        settings.Save();
                        net.Connect(settings.LastServer.Trim(), port);
                    }
                }
                if (!string.IsNullOrEmpty(net.LastError)) GUILayout.Label("<color=#ff8080>" + net.LastError + "</color>");
                else if (net.Status != "Not connected") GUILayout.Label(net.Status);
            }
            else
            {
                GUILayout.Label(net.Status);
                if (net.IsConnected) DrawLobby();
                if (GUILayout.Button("Disconnect")) net.Disconnect("user");
            }

            GUI.DragWindow();
        }

        private void DrawLobby()
        {
            var net = _addon.Network;
            GUILayout.Label("<b>Players online (" + _addon.Players.Count + ")</b>");
            foreach (var p in _addon.Players.Players)
                GUILayout.Label("  " + p.Name + (p.ClientId == net.ClientId ? "  (you)" : "  " + p.PingMs + " ms"));

            GUILayout.Space(6);
            _chat.Draw(160);
            GUILayout.Space(6);

            var ut = _addon.TimeSync.HasSync ? _addon.TimeSync.ServerUt : net.Welcome.UniversalTime;
            GUILayout.Label("Server time: " + KSPUtil.PrintDateCompact(ut, true) + "   (UT " + ut.ToString("F0") + ")");
            if (GUILayout.Button("Enter game"))
            {
                try
                {
                    SessionStarter.EnterGame(ut);
                }
                catch (System.Exception e)
                {
                    Log.Exception("Enter game", e);
                    _addon.Chat.AddLocal("Could not start the game: " + e.Message);
                }
            }
        }
    }
}
