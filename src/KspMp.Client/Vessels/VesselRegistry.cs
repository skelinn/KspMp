using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspMp.Vessels
{
    /// <summary>All vessels this client knows about, who owns them, and which ones are driven by replicas.</summary>
    public sealed class VesselRegistry
    {
        public const float TombstoneSeconds = 2.5f;

        private readonly Dictionary<Guid, RemoteVessel> _vessels = new Dictionary<Guid, RemoteVessel>();
        private readonly Dictionary<Guid, float> _tombstones = new Dictionary<Guid, float>();

        /// <summary>Our client id once connected (0 otherwise).</summary>
        public int LocalClientId { get; set; }

        public IEnumerable<RemoteVessel> All => _vessels.Values;
        public int Count => _vessels.Count;

        public int CountOwnedByMe
        {
            get
            {
                var n = 0;
                foreach (var v in _vessels.Values) if (IsMine(v)) n++;
                return n;
            }
        }

        public int CountReplicas
        {
            get
            {
                var n = 0;
                foreach (var v in _vessels.Values) if (v.Replica != null) n++;
                return n;
            }
        }

        public bool TryGet(Guid id, out RemoteVessel vessel) => _vessels.TryGetValue(id, out vessel);
        public bool IsKnown(Guid id) => _vessels.ContainsKey(id);

        public RemoteVessel GetOrAdd(Guid id)
        {
            if (!_vessels.TryGetValue(id, out var vessel)) _vessels[id] = vessel = new RemoteVessel { Id = id };
            return vessel;
        }

        public bool Remove(Guid id)
        {
            if (!_vessels.TryGetValue(id, out var vessel)) return false;
            vessel.Replica?.Detach();
            vessel.Replica = null;
            return _vessels.Remove(id);
        }

        public int OwnerOf(Guid id) => _vessels.TryGetValue(id, out var vessel) ? vessel.OwnerClientId : 0;
        public bool IsMine(Guid id) => _vessels.TryGetValue(id, out var vessel) && IsMine(vessel);
        public bool IsMine(RemoteVessel vessel) => LocalClientId != 0 && vessel.OwnerClientId == LocalClientId;
        public bool IsOwnedByOther(Guid id) => _vessels.TryGetValue(id, out var vessel) && IsOwnedByOther(vessel);
        public bool IsOwnedByOther(RemoteVessel vessel) => vessel.OwnerClientId != 0 && vessel.OwnerClientId != LocalClientId;
        public bool IsUnowned(Guid id) => OwnerOf(id) == 0;

        public void Tombstone(Guid id) => _tombstones[id] = Time.realtimeSinceStartup;

        /// <summary>True for a short while after a vessel was removed, so late snapshots cannot resurrect it.</summary>
        public bool IsTombstoned(Guid id)
        {
            if (!_tombstones.TryGetValue(id, out var at)) return false;
            if (Time.realtimeSinceStartup - at <= TombstoneSeconds) return true;
            _tombstones.Remove(id);
            return false;
        }

        /// <summary>Creates or drops the replica for a vessel depending on who owns it and whether it exists in the game.</summary>
        public void SyncReplica(RemoteVessel vessel)
        {
            var kspVessel = FlightGlobals.fetch != null ? FlightGlobals.FindVessel(vessel.Id) : null;
            if (IsOwnedByOther(vessel) && kspVessel != null)
            {
                if (vessel.Replica == null)
                {
                    vessel.Replica = new Replica(vessel.Id);
                    if (vessel.HasState) vessel.Replica.Push(vessel.LastState);
                }
                if (kspVessel.loaded) VesselImmortal.Set(kspVessel, true);
            }
            else if (vessel.Replica != null)
            {
                vessel.Replica.Detach();
                vessel.Replica = null;
            }
        }

        public void Clear()
        {
            foreach (var vessel in _vessels.Values) vessel.Replica?.Detach();
            _vessels.Clear();
            _tombstones.Clear();
        }
    }
}
