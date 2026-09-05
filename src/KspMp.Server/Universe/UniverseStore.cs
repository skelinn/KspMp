using System;
using System.Collections.Generic;
using System.IO;
using KspMp.Shared.Config;

namespace KspMp.Server.Universe
{
    public sealed class KnownPlayer
    {
        public Guid PlayerId;
        public string Name;
        public string AvatarKerbalName;
        public DateTime LastSeenUtc;
    }

    /// <summary>
    /// The server's on-disk world: time.cfg, players.cfg and (from M2) vessels/ and roster/.
    /// A null directory means in-memory only (tests, throwaway sessions).
    /// </summary>
    public sealed class UniverseStore
    {
        public string Dir { get; }

        public UniverseStore(string dir)
        {
            Dir = dir;
            if (dir != null) Directory.CreateDirectory(dir);
        }

        public bool IsPersistent => Dir != null;

        private string PathOf(string file) => Path.Combine(Dir, file);

        // ---- time ----

        public bool TryLoadTime(out double universalTime, out float rate)
        {
            universalTime = 0;
            rate = 1f;
            if (!IsPersistent || !File.Exists(PathOf("time.cfg"))) return false;
            var node = CfgNode.Load(PathOf("time.cfg")).GetNode("TIME");
            if (node == null) return false;
            universalTime = node.GetDouble("ut", 0);
            rate = node.GetFloat("rate", 1f);
            return true;
        }

        public void SaveTime(double universalTime, float rate)
        {
            if (!IsPersistent) return;
            var root = new CfgNode();
            var node = root.AddNode("TIME");
            node.AddValue("ut", universalTime);
            node.AddValue("rate", rate);
            node.AddValue("savedAtUtc", DateTime.UtcNow.ToString("o"));
            root.Save(PathOf("time.cfg"));
        }

        // ---- vessels ----

        private string VesselsDir => Path.Combine(Dir, "vessels");

        /// <summary>Reads every vessels/*.cfg as (id from file name, ConfigNode text).</summary>
        public IEnumerable<KeyValuePair<Guid, string>> LoadVesselTexts()
        {
            if (!IsPersistent || !Directory.Exists(VesselsDir)) yield break;
            foreach (var file in Directory.GetFiles(VesselsDir, "*.cfg"))
            {
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id)) continue;
                yield return new KeyValuePair<Guid, string>(id, File.ReadAllText(file));
            }
        }

        public void SaveVesselText(Guid id, string text)
        {
            if (!IsPersistent) return;
            Directory.CreateDirectory(VesselsDir);
            var path = Path.Combine(VesselsDir, id + ".cfg");
            File.WriteAllText(path + ".tmp", text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        public void DeleteVessel(Guid id)
        {
            if (!IsPersistent) return;
            var path = Path.Combine(VesselsDir, id + ".cfg");
            if (File.Exists(path)) File.Delete(path);
        }

        // ---- roster ----

        private string RosterDir => Path.Combine(Dir, "roster");

        private static string KerbalFileName(string name)
        {
            var safe = new System.Text.StringBuilder(name.Length);
            foreach (var c in name) safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ' ? c : '_');
            return safe.ToString().Trim() + ".cfg";
        }

        public IEnumerable<KeyValuePair<string, string>> LoadKerbalTexts()
        {
            if (!IsPersistent || !Directory.Exists(RosterDir)) yield break;
            foreach (var file in Directory.GetFiles(RosterDir, "*.cfg"))
            {
                var text = File.ReadAllText(file);
                var node = CfgNode.Parse(text);
                var kerbal = node.GetNode("KERBAL") ?? node;
                var name = kerbal.GetValue("name");
                if (string.IsNullOrEmpty(name)) continue;
                yield return new KeyValuePair<string, string>(name, text);
            }
        }

        public void SaveKerbalText(string name, string text)
        {
            if (!IsPersistent) return;
            Directory.CreateDirectory(RosterDir);
            var path = Path.Combine(RosterDir, KerbalFileName(name));
            File.WriteAllText(path + ".tmp", text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        public void DeleteKerbal(string name)
        {
            if (!IsPersistent) return;
            var path = Path.Combine(RosterDir, KerbalFileName(name));
            if (File.Exists(path)) File.Delete(path);
        }

        // ---- players ----

        public Dictionary<Guid, KnownPlayer> LoadPlayers()
        {
            var players = new Dictionary<Guid, KnownPlayer>();
            if (!IsPersistent || !File.Exists(PathOf("players.cfg"))) return players;
            foreach (var node in CfgNode.Load(PathOf("players.cfg")).GetNodes("PLAYER"))
            {
                var id = node.GetGuid("id");
                if (id == Guid.Empty) continue;
                DateTime.TryParse(node.GetValue("lastSeenUtc"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastSeen);
                players[id] = new KnownPlayer
                {
                    PlayerId = id,
                    Name = node.GetValue("name") ?? "Player",
                    AvatarKerbalName = node.GetValue("kerbal") ?? string.Empty,
                    LastSeenUtc = lastSeen,
                };
            }
            return players;
        }

        public void SavePlayers(IEnumerable<KnownPlayer> players)
        {
            if (!IsPersistent) return;
            var root = new CfgNode();
            foreach (var p in players)
            {
                var node = root.AddNode("PLAYER");
                node.AddValue("id", p.PlayerId);
                node.AddValue("name", p.Name ?? string.Empty);
                node.AddValue("kerbal", p.AvatarKerbalName ?? string.Empty);
                node.AddValue("lastSeenUtc", p.LastSeenUtc.ToString("o"));
            }
            root.Save(PathOf("players.cfg"));
        }
    }
}
