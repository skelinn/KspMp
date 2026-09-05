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
        public int Port = 7777;
        public string AvatarKerbalName = "";
        public float UiScale = 1f;
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
                        node.TryGetValue("port", ref settings.Port);
                        node.TryGetValue("avatarKerbalName", ref settings.AvatarKerbalName);
                        node.TryGetValue("uiScale", ref settings.UiScale);
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
                node.AddValue("port", Port);
                node.AddValue("avatarKerbalName", AvatarKerbalName);
                node.AddValue("uiScale", UiScale);
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
