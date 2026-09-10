using System;
using System.Collections.Generic;

namespace KspMp.Net
{
    /// <summary>
    /// What this install can build with, in a form two machines can compare: the number of loaded parts and
    /// a hash of their names together with the modules on each of them. Two players whose parts differ cannot
    /// load each other's craft, and KSP fails that quietly and late; the handshake carries this so the server
    /// can say so at once.
    ///
    /// The modules matter as much as the names. A mod like MechJeb is bolted onto every command pod by a
    /// ModuleManager patch, so two installs where only one has it hold the very same parts under the very
    /// same names - and a relayed part-menu action, which names a module by its index, then lands on the
    /// wrong module or on none at all. Hashing the modules catches that at the handshake.
    /// </summary>
    internal static class GameDataSignature
    {
        private static int _count = -1;
        private static string _hash = "";

        public static int PartCount { get { Compute(); return _count; } }
        public static string PartsHash { get { Compute(); return _hash; } }

        private static void Compute()
        {
            if (_count >= 0) return;
            try
            {
                var names = new List<string>();
                var parts = PartLoader.LoadedPartsList;
                var modules = new List<string>();
                if (parts != null)
                    foreach (var part in parts)
                    {
                        if (part == null || string.IsNullOrEmpty(part.name)) continue;
                        modules.Clear();
                        var prefab = part.partPrefab;
                        if (prefab != null && prefab.Modules != null)
                            for (var i = 0; i < prefab.Modules.Count; i++)
                            {
                                var module = prefab.Modules[i];
                                if (module != null && !string.IsNullOrEmpty(module.moduleName)) modules.Add(module.moduleName);
                            }
                        modules.Sort(StringComparer.Ordinal);
                        names.Add(part.name + "|" + string.Join(",", modules.ToArray()));
                    }
                names.Sort(StringComparer.Ordinal);
                // FNV-1a over the sorted names: stable across runs and machines, cheap, and not a secret.
                var hash = 2166136261u;
                foreach (var name in names)
                {
                    foreach (var c in name) { hash ^= c; hash *= 16777619u; }
                    hash ^= '\n'; hash *= 16777619u;
                }
                _count = names.Count;
                _hash = hash.ToString("x8");
                Log.Info("Installed parts: " + _count + " (with their modules), signature " + _hash);
            }
            catch (Exception e)
            {
                Log.Exception("Computing the parts signature", e);
                _count = 0;
                _hash = "";
            }
        }
    }
}
