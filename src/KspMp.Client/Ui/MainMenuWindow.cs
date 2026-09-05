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
        private AvatarPanel _avatar;
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

                GUILayout.BeginHorizontal();
                GUILayout.Label("Password", GUILayout.Width(60));
                settings.LastPassword = GUILayout.PasswordField(settings.LastPassword ?? string.Empty, '*', 32);
                GUILayout.EndHorizontal();
                GUILayout.Label("<i>Leave blank unless the host set one.</i>");

                if (GUILayout.Button("Connect"))
                {
                    if (int.TryParse(_port, out var port) && port > 0 && port < 65536)
                    {
                        settings.Port = port;
                        settings.Save();
                        net.Password = settings.LastPassword ?? string.Empty;
                        net.Connect(settings.LastServer.Trim(), port);
                    }
                }
                DrawSteam(net, settings);

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

        /// <summary>
        /// Playing over Steam, for people who cannot forward a port and should not have to. Steam finds a
        /// route between the two games and falls back to its own relay when it cannot make a direct one.
        /// Hidden entirely when Steam is not there, since nothing here would work and the reason is not the
        /// player's problem to solve at the main menu.
        /// </summary>
        private void DrawSteam(ClientNetwork net, Settings settings)
        {
            GUILayout.Space(8);
            if (!Net.Steam.SteamP2P.TryInitialise())
            {
                GUILayout.Label("<i>Steam play unavailable: " + Net.Steam.SteamP2P.Unavailable + "</i>");
                return;
            }

            GUILayout.Label("<b>Play over Steam</b>  <i>(no port forwarding)</i>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Your ID", GUILayout.Width(60));
            // Selectable rather than a label, so it can be copied out and sent to a friend.
            GUILayout.TextField(Net.Steam.SteamP2P.LocalSteamId.ToString());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Join", GUILayout.Width(60));
            settings.LastSteamHost = GUILayout.TextField(settings.LastSteamHost ?? string.Empty, 20);
            if (GUILayout.Button("Join", GUILayout.Width(70)))
            {
                if (ulong.TryParse((settings.LastSteamHost ?? string.Empty).Trim(), out var hostId) && hostId != 0)
                {
                    settings.Save();
                    net.Password = settings.LastPassword ?? string.Empty;
                    net.ConnectOverSteam(hostId);
                }
                else Log.Warn("That does not look like a Steam ID: " + settings.LastSteamHost);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Friends", GUILayout.Width(60));
            settings.AllowedSteamIds = GUILayout.TextField(settings.AllowedSteamIds ?? string.Empty);
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>Steam IDs allowed into a game you host, comma separated. Steam needs them "
                            + "before it will accept anything from them.</i>");

            if (_addon.Host != null && _addon.Host.Running)
            {
                GUILayout.Label("<b>Hosting.</b> Friends join with your ID above.");
            }
            else if (GUILayout.Button("Host a game"))
            {
                settings.Save();
                _addon.StartHosting(ParseSteamIds(settings.AllowedSteamIds));
            }
        }

        /// <summary>Reads the friends box, ignoring anything that is not a Steam ID rather than refusing it all.</summary>
        private static System.Collections.Generic.List<ulong> ParseSteamIds(string text)
        {
            var ids = new System.Collections.Generic.List<ulong>();
            if (string.IsNullOrEmpty(text)) return ids;
            foreach (var part in text.Split(','))
                if (ulong.TryParse(part.Trim(), out var id) && id != 0) ids.Add(id);
            return ids;
        }

        private void DrawLobby()
        {
            var net = _addon.Network;
            GUILayout.Label("<b>Players online (" + _addon.Players.Count + ")</b>");
            foreach (var p in _addon.Players.Players)
                GUILayout.Label("  " + p.Name + (string.IsNullOrEmpty(p.AvatarKerbalName) ? "" : " as " + p.AvatarKerbalName) + (p.ClientId == net.ClientId ? "  (you)" : "  " + p.PingMs + " ms")
                                + (p.ClientId != net.ClientId ? "  " + _addon.Presence.Describe(p.ClientId) : ""));

            GUILayout.Space(6);
            _chat.Draw(160);
            GUILayout.Space(6);

            var ut = _addon.TimeSync.HasSync ? _addon.TimeSync.ServerUt : net.Welcome.UniversalTime;
            GUILayout.Label("Server time: " + KSPUtil.PrintDateCompact(ut, true) + "   (UT " + ut.ToString("F0") + ")");
            var roster = _addon.Roster;
            if (roster.NeedsAvatar)
            {
                if (_avatar == null) _avatar = new AvatarPanel(_addon);
                _avatar.Draw();
                return;
            }
            GUILayout.Label("Your Kerbal: <b>" + roster.AvatarName + "</b>   roster: " + roster.Count + " kerbal(s), " + _addon.Vessels.Count + " vessel(s)" + (roster.Synced ? "" : "  (syncing ...)"));
            GUI.enabled = roster.Synced;
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
            GUI.enabled = true;
        }
    }
}
