using KspMp.Game;
using KspMp.Net;
using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>Main-menu window: connect to a server, then the lobby (players, chat, Enter game).</summary>
    internal sealed class MainMenuWindow
    {
        private const float Width = 500f;
        private const float LabelColumn = 74f;
        private static readonly int WindowId = "KspMp.MainMenu".GetHashCode();
        private readonly KspMpAddon _addon;
        private readonly ChatPanel _chat;
        private AvatarPanel _avatar;
        private Rect _rect = new Rect(40, 130, Width, 0);
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
            Theme.Begin();
            _rect = Theme.Clamp(GUILayout.Window(WindowId, _rect, DrawContents,
                                     "KspMp" + Theme.Tint("   " + KspMpAddon.Version, Theme.Dim), GUILayout.Width(Width)));
            Theme.End();
        }

        private void DrawContents(int id)
        {
            var net = _addon.Network;
            var settings = _addon.Settings;

            DrawStatusStrip(net);

            if (net.State == ConnectionState.Disconnected)
            {
                DrawDirectConnect(net, settings);
                DrawSteam(net, settings);
            }
            else
            {
                if (net.IsConnected) DrawLobby();
                if (GUILayout.Button("Disconnect")) net.Disconnect("user");
            }

            DrawSizeRow();
            Theme.DragHeader();
        }

        /// <summary>
        /// The one setting worth having in the window rather than in a config file: on a large screen the
        /// windows are drawn small, and someone who cannot read them cannot go looking for the file either.
        /// </summary>
        private void DrawSizeRow()
        {
            Theme.Separator();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Interface size", Theme.Key);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("-", GUILayout.Width(32))) Resize(-0.1f);
            GUILayout.Label(Mathf.RoundToInt(Theme.Scale * 100) + "%", Theme.Chip, GUILayout.Width(52));
            if (GUILayout.Button("+", GUILayout.Width(32))) Resize(0.1f);
            GUILayout.EndHorizontal();
        }

        private void Resize(float by)
        {
            var settings = _addon.Settings;
            settings.InterfaceScale = Mathf.Clamp(Mathf.Round((Theme.Scale + by) * 20f) / 20f, 0.7f, 2.5f);
            Theme.SetScale(settings.InterfaceScale);
            settings.Save();
        }

        /// <summary>
        /// One line that always says where things stand, in the same place whatever the window is showing.
        /// Colour does the work: green is connected, amber is in progress, red is a problem worth reading.
        /// </summary>
        private void DrawStatusStrip(ClientNetwork net)
        {
            var hosting = _addon.Host != null && _addon.Host.Running;
            Color colour;
            string text;
            if (!string.IsNullOrEmpty(net.LastError) && net.State == ConnectionState.Disconnected)
            {
                colour = Theme.Bad;
                text = net.LastError;
            }
            else
            {
                switch (net.State)
                {
                    case ConnectionState.Connected: colour = Theme.Accent; break;
                    case ConnectionState.Disconnected: colour = hosting ? Theme.Accent : Theme.Dim; break;
                    default: colour = Theme.Warn; break;
                }
                text = net.Status;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(Theme.Dot(colour) + "  " + Theme.Tint(text, colour), Theme.Value);
            GUILayout.FlexibleSpace();
            if (hosting) GUILayout.Label(Theme.Tint("HOSTING", Theme.Accent), Theme.Chip);
            GUILayout.EndHorizontal();
            Theme.Separator();
        }

        private void DrawDirectConnect(ClientNetwork net, Settings settings)
        {
            Theme.BeginSection("DIRECT CONNECT");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Your name", Theme.FieldKey, GUILayout.Width(LabelColumn));
            settings.PlayerName = GUILayout.TextField(settings.PlayerName, 24);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", Theme.FieldKey, GUILayout.Width(LabelColumn));
            settings.LastServer = GUILayout.TextField(settings.LastServer);
            GUILayout.Label("Port", Theme.FieldKey, GUILayout.Width(32));
            _port = GUILayout.TextField(_port, 5, GUILayout.Width(64));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Password", Theme.FieldKey, GUILayout.Width(LabelColumn));
            settings.LastPassword = GUILayout.PasswordField(settings.LastPassword ?? string.Empty, '*', 32);
            GUILayout.EndHorizontal();
            GUILayout.Label("Leave blank unless the host set one.", Theme.Caption);

            if (GUILayout.Button("Connect", Theme.Primary))
            {
                if (int.TryParse(_port, out var port) && port > 0 && port < 65536)
                {
                    settings.Port = port;
                    settings.Save();
                    net.Password = settings.LastPassword ?? string.Empty;
                    net.Connect(settings.LastServer.Trim(), port);
                }
            }

            Theme.EndSection();
        }

        /// <summary>
        /// Playing over Steam, for people who cannot forward a port and should not have to. Steam finds a
        /// route between the two games and falls back to its own relay when it cannot make a direct one.
        /// Hidden entirely when Steam is not there, since nothing here would work and the reason is not the
        /// player's problem to solve at the main menu.
        /// </summary>
        private void DrawSteam(ClientNetwork net, Settings settings)
        {
            if (!Net.Steam.SteamP2P.TryInitialise())
            {
                Theme.BeginSection("PLAY OVER STEAM");
                GUILayout.Label("Unavailable: " + Net.Steam.SteamP2P.Unavailable, Theme.Caption);
                Theme.EndSection();
                return;
            }

            Theme.BeginSection("PLAY OVER STEAM");
            GUILayout.Label("Nobody has to forward a port. Swap IDs with a friend and go.", Theme.Caption);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Your ID", Theme.FieldKey, GUILayout.Width(LabelColumn));
            // Selectable rather than a label, so it can be copied out and sent to a friend.
            GUILayout.TextField(Net.Steam.SteamP2P.LocalSteamId.ToString());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Join", Theme.FieldKey, GUILayout.Width(LabelColumn));
            settings.LastSteamHost = GUILayout.TextField(settings.LastSteamHost ?? string.Empty, 20);
            if (GUILayout.Button("Join", GUILayout.Width(76)))
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

            var hosting = _addon.Host != null && _addon.Host.Running;

            Theme.Separator();
            GUILayout.Label("HOST A GAME", Theme.Head);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Let in", Theme.FieldKey, GUILayout.Width(LabelColumn));
            settings.AllowedSteamIds = GUILayout.TextField(settings.AllowedSteamIds ?? string.Empty);
            // While hosting, changes to the list can be applied straight away. Steam accepts a session on
            // demand, so a friend added mid-game gets in without anyone restarting anything.
            GUI.enabled = hosting;
            if (GUILayout.Button("Apply", GUILayout.Width(76)))
            {
                settings.Save();
                var added = 0;
                foreach (var id in ParseSteamIds(settings.AllowedSteamIds)) if (_addon.Host.Allow(id)) added++;
                Log.Info("Allowed " + added + " new Steam player(s) into the hosted game.");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Steam IDs allowed into your game, comma separated. Steam needs them before it "
                            + "will accept anything from those players.", Theme.Caption);

            if (hosting)
            {
                GUILayout.Label(Theme.Dot(Theme.Accent) + "  Hosting. Friends join with your ID above; add one "
                                + "to the list and press Apply to let them in without restarting.", Theme.Value);
                if (GUILayout.Button("Stop hosting")) _addon.StopHosting();
            }
            else if (GUILayout.Button("Host a game", Theme.Primary))
            {
                settings.Save();
                _addon.StartHosting(ParseSteamIds(settings.AllowedSteamIds));
            }

            Theme.EndSection();
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

            Theme.BeginSection("PLAYERS  (" + _addon.Players.Count + ")");
            foreach (var p in _addon.Players.Players)
            {
                var you = p.ClientId == net.ClientId;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Theme.Dot(Theme.PlayerColour(p.ClientId)) + "  " + p.Name
                                + (string.IsNullOrEmpty(p.AvatarKerbalName) ? "" : Theme.Tint("  as " + p.AvatarKerbalName, Theme.Dim)),
                                Theme.Value);
                GUILayout.FlexibleSpace();
                GUILayout.Label(you ? "you" : p.PingMs + " ms", Theme.Key);
                GUILayout.EndHorizontal();
                if (!you) GUILayout.Label("      " + _addon.Presence.Describe(p.ClientId), Theme.Caption);
            }
            Theme.EndSection();

            Theme.BeginSection("CHAT");
            _chat.Draw(160);
            Theme.EndSection();

            var ut = _addon.TimeSync.HasSync ? _addon.TimeSync.ServerUt : net.Welcome.UniversalTime;
            var roster = _addon.Roster;

            if (roster.NeedsAvatar)
            {
                if (_avatar == null) _avatar = new AvatarPanel(_addon);
                _avatar.Draw();
                return;
            }

            Theme.BeginSection("WORLD");
            Theme.Row("Server time", KSPUtil.PrintDateCompact(ut, true) + Theme.Tint("   UT " + ut.ToString("F0"), Theme.Dim));
            Theme.Row("Your Kerbal", roster.AvatarName);
            Theme.Row("Roster", roster.Count + " kerbal(s), " + _addon.Vessels.Count + " vessel(s)"
                                + (roster.Synced ? "" : Theme.Tint("   syncing ...", Theme.Warn)));
            Theme.EndSection();

            GUI.enabled = roster.Synced;
            if (GUILayout.Button(roster.Synced ? "Enter game" : "Waiting for the world ...", Theme.Primary))
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
