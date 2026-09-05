using System.IO;
using KspMp.Shared.Config;

namespace KspMp.Server
{
    /// <summary>Server settings, stored as &lt;universe&gt;/server.cfg in KSP ConfigNode format.</summary>
    public sealed class ServerConfig
    {
        public const string FileName = "server.cfg";

        public string ServerName = "KspMp server";
        public int Port = 7777;
        public int MaxPlayers = 16;
        public string MessageOfTheDay = "";
        /// <summary>Players must know this to join. Empty means anyone who can reach the port can play.</summary>
        public string Password = "";
        public bool HostControlsWarp = false;
        /// <summary>Ask the router to forward <see cref="Port"/> on startup, so hosting from home needs no router setup.</summary>
        public bool Upnp = true;
        public float RespawnSeconds = 7200f;
        public bool SharedStickDefault = false;
        public double InitialUniversalTime = 0;

        /// <summary>Loads the config from the universe directory, writing defaults first if the file does not exist.</summary>
        public static ServerConfig Load(string universeDir)
        {
            var config = new ServerConfig();
            if (universeDir == null) return config;
            var path = Path.Combine(universeDir, FileName);
            if (!File.Exists(path))
            {
                config.Save(universeDir);
                return config;
            }
            var node = CfgNode.Load(path).GetNode("SERVER");
            if (node == null) return config;
            config.ServerName = node.GetValue("name") ?? config.ServerName;
            config.Port = node.GetInt("port", config.Port);
            config.MaxPlayers = node.GetInt("maxPlayers", config.MaxPlayers);
            config.MessageOfTheDay = node.GetValue("motd") ?? config.MessageOfTheDay;
            config.Password = node.GetValue("password") ?? config.Password;
            config.HostControlsWarp = node.GetBool("hostControlsWarp", config.HostControlsWarp);
            config.Upnp = node.GetBool("upnp", config.Upnp);
            config.RespawnSeconds = node.GetFloat("respawnSeconds", config.RespawnSeconds);
            config.SharedStickDefault = node.GetBool("sharedStickDefault", config.SharedStickDefault);
            config.InitialUniversalTime = node.GetDouble("initialUniversalTime", config.InitialUniversalTime);
            return config;
        }

        public void Save(string universeDir)
        {
            var root = new CfgNode();
            var node = root.AddNode("SERVER");
            node.AddValue("name", ServerName);
            node.AddValue("port", Port);
            node.AddValue("maxPlayers", MaxPlayers);
            node.AddValue("motd", MessageOfTheDay);
            node.AddValue("password", Password);
            node.AddValue("hostControlsWarp", HostControlsWarp);
            node.AddValue("upnp", Upnp);
            node.AddValue("respawnSeconds", RespawnSeconds);
            node.AddValue("sharedStickDefault", SharedStickDefault);
            node.AddValue("initialUniversalTime", InitialUniversalTime);
            root.Save(Path.Combine(universeDir, FileName));
        }
    }
}
