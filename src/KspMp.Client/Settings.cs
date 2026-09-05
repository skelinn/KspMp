using System;
using System.IO;

namespace KspMp
{
    /// <summary>Per-install settings in GameData/KspMp/PluginData/settings.cfg (KSP ConfigNode format).</summary>
    public sealed class Settings
    {
        private const string NodeName = "KSPMP_SETTINGS";

        public string PlayerName = "Kerbonaut";
        public Guid PlayerId = Guid.Empty;
        public string LastServer = "127.0.0.1";
        /// <summary>Password for LastServer, so it does not have to be retyped every session.</summary>
        public string LastPassword = "";
        /// <summary>Steam ID of the last game joined over Steam.</summary>
        public string LastSteamHost = "";
        /// <summary>Comma-separated Steam IDs allowed into a game we host; Steam needs them up front.</summary>
        public string AllowedSteamIds = "";
        public int Port = 7777;
        public string AvatarKerbalName = "";
        /// <summary>
        /// How large the mod's windows are drawn. Zero means work it out from the screen, which is what a
        /// fresh install gets: IMGUI draws in raw pixels, so the same window is half the size on a 1440p
        /// screen as on a 720p one unless something scales it.
        /// </summary>
        public float InterfaceScale = 0f;
        public int LogLevel = 1;
        public bool ShowDebugWindow = false;
        public bool ShowHud = true;

        public static string FilePath => Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KspMp", "PluginData", "settings.cfg");

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    var root = ConfigNode.Load(FilePath);
                    // Builds before the wrapper fix saved the settings node straight to the file, which
                    // writes its values without the enclosing node; fall back to reading them from the root.
                    var node = root == null ? null : (root.GetNode(NodeName) ?? root);
                    if (node != null)
                    {
                        node.TryGetValue("playerName", ref settings.PlayerName);
                        var id = string.Empty;
                        if (node.TryGetValue("playerId", ref id) && Guid.TryParse(id, out var guid)) settings.PlayerId = guid;
                        node.TryGetValue("lastServer", ref settings.LastServer);
                        node.TryGetValue("lastPassword", ref settings.LastPassword);
                        node.TryGetValue("lastSteamHost", ref settings.LastSteamHost);
                        node.TryGetValue("allowedSteamIds", ref settings.AllowedSteamIds);
                        node.TryGetValue("port", ref settings.Port);
                        node.TryGetValue("avatarKerbalName", ref settings.AvatarKerbalName);
                        node.TryGetValue("interfaceScale", ref settings.InterfaceScale);
                        node.TryGetValue("logLevel", ref settings.LogLevel);
                        node.TryGetValue("showDebugWindow", ref settings.ShowDebugWindow);
                        node.TryGetValue("showHud", ref settings.ShowHud);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Exception("Loading settings", e);
            }

            if (settings.PlayerId == Guid.Empty)
            {
                settings.PlayerId = Guid.NewGuid();
                settings.Save();
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                var node = new ConfigNode(NodeName);
                node.AddValue("playerName", PlayerName);
                node.AddValue("playerId", PlayerId.ToString());
                node.AddValue("lastServer", LastServer);
                node.AddValue("lastPassword", LastPassword);
                node.AddValue("lastSteamHost", LastSteamHost);
                node.AddValue("allowedSteamIds", AllowedSteamIds);
                node.AddValue("port", Port);
                node.AddValue("avatarKerbalName", AvatarKerbalName);
                node.AddValue("interfaceScale", InterfaceScale);
                node.AddValue("logLevel", LogLevel);
                node.AddValue("showDebugWindow", ShowDebugWindow);
                node.AddValue("showHud", ShowHud);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                // Saving the node directly would write only its values, and Load looks for the node by
                // name, so the settings would never read back. Wrap it in a root node.
                var file = new ConfigNode();
                file.AddNode(node);
                file.Save(FilePath);
            }
            catch (Exception e)
            {
                Log.Exception("Saving settings", e);
            }
        }
    }
}
