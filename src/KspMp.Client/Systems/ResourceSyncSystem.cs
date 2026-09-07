using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Keeps a co-pilot's copy of the rocket on the same fuel as the pilot's.
    ///
    /// Everyone aboard a vessel has their own copy of it, and only the pilot's is really burning anything: the
    /// others mirror the pilot's staging and throttle, so their engines run too, but on tanks that drain at
    /// their own rate and never see a transfer, a refuel or a difference in flow. Left alone, a co-pilot's
    /// gauges disagree with the pilot's within a minute. The vessel you sit in is never reloaded from
    /// snapshots (that is your seat), so this streams the amounts instead: once a second the pilot sends what
    /// changed, and everyone aboard writes it straight into their tanks.
    /// </summary>
    public sealed class ResourceSyncSystem : SystemBase
    {
        public const float IntervalSeconds = 1f;
        /// <summary>Everything is sent again this often, so a lost delta cannot leave a tank wrong for good.</summary>
        public const float FullSendSeconds = 15f;

        private readonly Dictionary<Guid, Dictionary<long, float>> _lastSent = new Dictionary<Guid, Dictionary<long, float>>();
        private readonly Dictionary<Guid, float> _lastFullAt = new Dictionary<Guid, float>();
        private readonly List<VesselResourcesMsg.PartResources> _parts = new List<VesselResourcesMsg.PartResources>();
        private readonly List<VesselResourcesMsg.Resource> _amounts = new List<VesselResourcesMsg.Resource>();
        private float _nextSendAt;
        private float _nextCheckAt;
        private float _nextPruneAt;
        private readonly HashSet<Guid> _hadAboard = new HashSet<Guid>();
        private static readonly string[] Checked = { "LiquidFuel", "Oxidizer", "SolidFuel", "ElectricCharge", "MonoPropellant" };

        public ResourceSyncSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Resources";
        public int Sent { get; private set; }
        public int Applied { get; private set; }

        public override bool ShouldRun(GameScenes scene, bool connected) => connected && scene == GameScenes.FLIGHT;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.VesselResources, OnResources);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.VesselResources, OnResources);
            _lastSent.Clear();
            _lastFullAt.Clear();
        }

        public override void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || !Net.IsConnected) return;
            var now = Time.realtimeSinceStartup;
            if (now >= _nextCheckAt)
            {
                _nextCheckAt = now + 10f;
                LogFuelCheck();
            }
            if (now < _nextSendAt) return;
            _nextSendAt = now + IntervalSeconds;

            if (now >= _nextPruneAt)
            {
                _nextPruneAt = now + 60f;
                foreach (var id in new List<Guid>(_lastSent.Keys))
                    if (FlightGlobals.FindVessel(id) == null) { _lastSent.Remove(id); _lastFullAt.Remove(id); _hadAboard.Remove(id); }
            }
            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel == null || vessel.id == Guid.Empty || vessel.parts == null) continue;
                if (!Addon.Vessels.IsMine(vessel.id))
                {
                    // Not ours (any more): the baseline dies with the ownership, so taking it back later starts
                    // with a full send instead of suppressing every tank that "has not moved" since long ago.
                    _lastSent.Remove(vessel.id); _lastFullAt.Remove(vessel.id); _hadAboard.Remove(vessel.id);
                    continue;
                }
                if (!Addon.Control.OthersAboard(vessel.id)) { _hadAboard.Remove(vessel.id); continue; }
                // Somebody just came aboard: everything, now, not whatever moved since the last delta.
                if (_hadAboard.Add(vessel.id)) _lastFullAt.Remove(vessel.id);
                Send(vessel, now);
            }
        }

        private void Send(Vessel vessel, float now)
        {
            if (!_lastSent.TryGetValue(vessel.id, out var last)) _lastSent[vessel.id] = last = new Dictionary<long, float>();
            var full = !_lastFullAt.TryGetValue(vessel.id, out var fullAt) || now - fullAt >= FullSendSeconds;
            if (full) _lastFullAt[vessel.id] = now;

            _parts.Clear();
            for (var p = 0; p < vessel.parts.Count; p++)
            {
                var part = vessel.parts[p];
                if (part == null || part.Resources == null || part.Resources.Count == 0) continue;
                _amounts.Clear();
                foreach (PartResource resource in part.Resources)
                {
                    if (resource == null) continue;
                    var amount = (float)resource.amount;
                    if (float.IsNaN(amount) || float.IsInfinity(amount)) continue;
                    var key = ((long)part.flightID << 32) ^ (uint)resource.resourceName.GetHashCode();
                    // A tank that has not moved by a thousandth of its capacity is not worth a byte.
                    if (!full && last.TryGetValue(key, out var was) && Math.Abs(was - amount) <= Math.Max(0.001f * (float)resource.maxAmount, 0.0005f)) continue;
                    last[key] = amount;
                    _amounts.Add(new VesselResourcesMsg.Resource { Name = resource.resourceName, Amount = amount });
                }
                if (_amounts.Count > 0) _parts.Add(new VesselResourcesMsg.PartResources { PartFlightId = part.flightID, Resources = _amounts.ToArray() });
            }
            if (_parts.Count == 0) return;

            Net.Send(MessageId.VesselResources, new VesselResourcesMsg { VesselId = vessel.id, Parts = _parts.ToArray() }, Channel.Bulk, Delivery.ReliableOrdered);
            Sent++;
        }

        /// <summary>
        /// The active vessel's tank totals, every ten seconds, in the same words on every client - so two logs
        /// can be laid side by side and the co-pilot's copy shown to be on the pilot's fuel.
        /// </summary>
        private void LogFuelCheck()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.parts == null) return;
            var totals = new double[Checked.Length];
            for (var p = 0; p < vessel.parts.Count; p++)
            {
                var part = vessel.parts[p];
                if (part == null || part.Resources == null) continue;
                foreach (PartResource resource in part.Resources)
                {
                    if (resource == null) continue;
                    var index = Array.IndexOf(Checked, resource.resourceName);
                    if (index >= 0) totals[index] += resource.amount;
                }
            }
            var text = "";
            for (var i = 0; i < Checked.Length; i++)
                if (totals[i] > 0) text += (text.Length > 0 ? ", " : "") + Checked[i] + " " + totals[i].ToString("F1");
            if (text.Length == 0) return;
            Log.Info("Fuel check: " + vessel.GetDisplayName() + " (" + (Addon.Vessels.IsMine(vessel.id) ? "ours" : "theirs") + "): " + text
                     + "; resources sent " + Sent + ", applied " + Applied);
        }

        private void OnResources(NetDataReader body)
        {
            var msg = Envelope.Read<VesselResourcesMsg>(body);
            if (msg.Parts == null || Addon.Vessels.IsMine(msg.VesselId)) return;
            var vessel = FlightGlobals.FindVessel(msg.VesselId);
            if (vessel == null || !vessel.loaded || vessel.parts == null) return;

            var byId = new Dictionary<uint, Part>(vessel.parts.Count);
            for (var i = 0; i < vessel.parts.Count; i++)
                if (vessel.parts[i] != null) byId[vessel.parts[i].flightID] = vessel.parts[i];

            var written = 0;
            foreach (var entry in msg.Parts)
            {
                if (entry.Resources == null || !byId.TryGetValue(entry.PartFlightId, out var part) || part.Resources == null) continue;
                foreach (var amount in entry.Resources)
                {
                    var resource = part.Resources.Get(amount.Name);
                    if (resource == null || float.IsNaN(amount.Amount) || float.IsInfinity(amount.Amount)) continue;   // never let a NaN into a tank
                    resource.amount = Math.Max(0.0, Math.Min(resource.maxAmount, amount.Amount));
                    written++;
                }
            }
            if (written > 0) Applied++;
        }
    }
}
