using System;
using System.Collections.Generic;
using KspMp.Server.Universe;
using KspMp.Shared.Config;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Roster
{
    public sealed class KerbalRecord
    {
        public string Name = "";
        public string NodeText = "";
        public byte Status;
        public double InactiveTimeEnd;
        public bool Dirty;
    }

    /// <summary>The shared KerbalRoster, one roster/&lt;name&gt;.cfg per kerbal. Avatar ownership lives in the player records.</summary>
    public sealed class RosterStore
    {
        private readonly Dictionary<string, KerbalRecord> _kerbals = new Dictionary<string, KerbalRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> _deleted = new HashSet<string>(StringComparer.Ordinal);
        private readonly UniverseStore _universe;

        public RosterStore(UniverseStore universe, Action<string> log)
        {
            _universe = universe;
            foreach (var pair in universe.LoadKerbalTexts())
            {
                try
                {
                    var record = new KerbalRecord { Name = pair.Key, NodeText = pair.Value };
                    var node = CfgNode.Parse(pair.Value);
                    var kerbal = node.GetNode("KERBAL") ?? node;
                    record.Status = ParseStatus(kerbal.GetValue("state"));
                    record.InactiveTimeEnd = kerbal.GetDouble("inactiveTimeEnd", 0);
                    _kerbals[record.Name] = record;
                }
                catch (Exception e)
                {
                    log("Skipping unreadable kerbal file " + pair.Key + ": " + e.Message);
                }
            }
        }

        public int Count => _kerbals.Count;
        public IEnumerable<KerbalRecord> All => _kerbals.Values;
        public bool TryGet(string name, out KerbalRecord record) => _kerbals.TryGetValue(name, out record);
        public bool Exists(string name) => _kerbals.ContainsKey(name);

        public KerbalRecord Upsert(string name, string nodeText)
        {
            if (!_kerbals.TryGetValue(name, out var record)) _kerbals[name] = record = new KerbalRecord { Name = name };
            record.NodeText = nodeText ?? string.Empty;
            var kerbal = CfgNode.Parse(record.NodeText);
            kerbal = kerbal.GetNode("KERBAL") ?? kerbal;
            record.Status = ParseStatus(kerbal.GetValue("state"));
            record.InactiveTimeEnd = kerbal.GetDouble("inactiveTimeEnd", record.InactiveTimeEnd);
            record.Dirty = true;
            _deleted.Remove(name);
            return record;
        }

        public bool UpdateStatus(string name, byte status, double inactiveTimeEnd)
        {
            if (!_kerbals.TryGetValue(name, out var record)) return false;
            record.Status = status;
            record.InactiveTimeEnd = inactiveTimeEnd;
            var node = CfgNode.Parse(record.NodeText);
            var kerbal = node.GetNode("KERBAL") ?? node;
            kerbal.SetValue("state", StatusName(status));
            kerbal.SetValue("inactiveTimeEnd", inactiveTimeEnd.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            record.NodeText = node.ToText();
            record.Dirty = true;
            return true;
        }

        public bool Remove(string name)
        {
            if (!_kerbals.Remove(name)) return false;
            _deleted.Add(name);
            return true;
        }

        public void SaveDirty()
        {
            if (!_universe.IsPersistent) return;
            foreach (var name in _deleted) _universe.DeleteKerbal(name);
            _deleted.Clear();
            foreach (var record in _kerbals.Values)
            {
                if (!record.Dirty) continue;
                _universe.SaveKerbalText(record.Name, record.NodeText);
                record.Dirty = false;
            }
        }

        public static byte ParseStatus(string state)
        {
            switch ((state ?? string.Empty).Trim())
            {
                case "Assigned": return 1;
                case "Dead": return 2;
                case "Missing": return 3;
                default: return 0;
            }
        }

        public static string StatusName(byte status)
        {
            switch (status)
            {
                case 1: return "Assigned";
                case 2: return "Dead";
                case 3: return "Missing";
                default: return "Available";
            }
        }
    }
}
