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
        public bool ShowDebugWindow = true;

        public static string FilePath => Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KspMp", "PluginData", "settings.cfg");

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    var node = ConfigNode.Load(FilePath)?.GetNode(NodeName);
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
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                node.Save(FilePath);
            }
            catch (Exception e)
            {
                Log.Exception("Saving settings", e);
            }
        }
    }
}
