using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KspMp.Server.Universe;
using KspMp.Shared.Codec;
using KspMp.Shared.Config;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Vessels
{
    /// <summary>All vessels in the universe. Persisted as readable ConfigNode text under &lt;universe&gt;/vessels/&lt;id&gt;.cfg.</summary>
    public sealed class VesselStore
    {
        private readonly Dictionary<Guid, VesselRecord> _vessels = new Dictionary<Guid, VesselRecord>();
        private readonly HashSet<Guid> _deleted = new HashSet<Guid>();
        private readonly UniverseStore _universe;
        private readonly Action<string> _log;

        public VesselStore(UniverseStore universe, Action<string> log)
        {
            _universe = universe;
            _log = log;
            foreach (var pair in universe.LoadVesselTexts())
            {
                try
                {
                    var record = FromText(pair.Key, pair.Value);
                    _vessels[record.Id] = record;
                }
                catch (Exception e)
                {
                    _log("Skipping unreadable vessel file " + pair.Key + ": " + e.Message);
                }
            }
        }

        public int Count => _vessels.Count;
        public IEnumerable<VesselRecord> All => _vessels.Values;

        public bool TryGet(Guid id, out VesselRecord record) => _vessels.TryGetValue(id, out record);

        public VesselRecord Upsert(VesselProtoMsg proto, double universalTime)
        {
            if (!_vessels.TryGetValue(proto.VesselId, out var record))
                _vessels[proto.VesselId] = record = new VesselRecord { Id = proto.VesselId };
            record.PersistentId = proto.PersistentId;
            record.Name = proto.Name ?? string.Empty;
            record.VesselType = proto.VesselType ?? string.Empty;
            record.ProtoDeflated = proto.ProtoDeflated ?? Array.Empty<byte>();
            record.Version++;
            record.UpdatedUt = universalTime;
            record.Dirty = true;
            _deleted.Remove(proto.VesselId);
            return record;
        }

        public void UpdateState(VesselStateMsg state)
        {
            if (!_vessels.TryGetValue(state.VesselId, out var record)) return;
            record.HasState = true;
            record.LastState = state;
        }

        public bool Remove(Guid id)
        {
            if (!_vessels.Remove(id)) return false;
            _deleted.Add(id);
            return true;
        }

        public void SaveDirty()
        {
            if (!_universe.IsPersistent) return;
            foreach (var id in _deleted) _universe.DeleteVessel(id);
            _deleted.Clear();
            foreach (var record in _vessels.Values)
            {
                if (!record.Dirty) continue;
                var text = Encoding.UTF8.GetString(DeflateCodec.Decompress(record.ProtoDeflated, 0, record.ProtoDeflated.Length));
                _universe.SaveVesselText(record.Id, text);
                record.Dirty = false;
            }
        }

        private static VesselRecord FromText(Guid id, string text)
        {
            var raw = Encoding.UTF8.GetBytes(text);
            var record = new VesselRecord { Id = id, ProtoDeflated = DeflateCodec.Compress(raw, 0, raw.Length) };
            var root = CfgNode.Parse(text);
            var vessel = root.GetNode("VESSEL") ?? root;
            record.Name = vessel.GetValue("name") ?? string.Empty;
            record.VesselType = vessel.GetValue("type") ?? string.Empty;
            record.PersistentId = (uint)vessel.GetLong("persistentId", 0);
            var pid = vessel.GetGuid("pid");
            if (pid != Guid.Empty && pid != id) record.Id = pid;
            return record;
        }
    }
}
