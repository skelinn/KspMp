using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using KspMp.Vessels;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Physics authority: who simulates which vessel. We ask for our active vessel and for any loaded vessel nobody
    /// owns; the server decides. Vessels owned by others are driven by replicas and, if we sit in one, we spectate.
    /// </summary>
    public sealed class AuthoritySystem : SystemBase
    {
        private const string SpectateLockId = "KspMp.spectate";
        private readonly Dictionary<Guid, float> _pendingRequests = new Dictionary<Guid, float>();
        private float _nextScanAt;
        private bool _spectating;

        public AuthoritySystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Authority";
        private VesselRegistry Registry => Addon.Vessels;
        public bool Spectating => _spectating;

        public string SpectatingOwnerName
        {
            get
            {
                var active = FlightGlobals.ActiveVessel;
                if (active == null || !Registry.TryGet(active.id, out var remote)) return "";
                return Addon.Players.TryGet(remote.OwnerClientId, out var player) ? player.Name : "#" + remote.OwnerClientId;
            }
        }

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.AuthorityAssign, OnAssign);
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.AuthorityAssign, OnAssign);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);
            _pendingRequests.Clear();
            SetSpectating(false);
        }

        public void Request(Guid vesselId)
        {
            if (vesselId == Guid.Empty || Registry.IsMine(vesselId) || !Net.IsConnected) return;
            if (_pendingRequests.TryGetValue(vesselId, out var at) && Time.realtimeSinceStartup - at < 2f) return;
            _pendingRequests[vesselId] = Time.realtimeSinceStartup;
            Net.Send(MessageId.AuthorityRequest, new AuthorityRequestMsg { VesselId = vesselId }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void Release(Guid vesselId)
        {
            if (!Registry.IsMine(vesselId)) return;
            Net.Send(MessageId.AuthorityRelease, new AuthorityReleaseMsg { VesselId = vesselId }, Channel.Control, Delivery.ReliableOrdered);
            Registry.GetOrAdd(vesselId).OwnerClientId = 0;
        }

        public void ReleaseAll(string why)
        {
            var mine = new List<Guid>();
            foreach (var remote in Registry.All)
                if (Registry.IsMine(remote)) mine.Add(remote.Id);
            if (mine.Count == 0) return;
            Log.Info("Releasing authority over " + mine.Count + " vessel(s): " + why);
            foreach (var id in mine) Release(id);
        }

        public override void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            var now = Time.realtimeSinceStartup;
            if (now < _nextScanAt) return;
            _nextScanAt = now + 1f;

            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel == null || vessel.id == Guid.Empty || Registry.IsTombstoned(vessel.id)) continue;
                if (!Registry.TryGet(vessel.id, out var remote))
                {
                    continue; // unknown here: VesselProtoSystem announces new local vessels
                }
                if (remote.OwnerClientId == 0)
                {
                    Request(vessel.id); // nobody simulates it and it is inside our physics range: volunteer
                }
                else
                {
                    Registry.SyncReplica(remote);
                }
            }
            UpdateSpectating();
        }

        private void OnAssign(NetDataReader body)
        {
            var msg = Envelope.Read<AuthorityAssignMsg>(body);
            var remote = Registry.GetOrAdd(msg.VesselId);
            var before = remote.OwnerClientId;
            remote.OwnerClientId = msg.OwnerClientId;
            _pendingRequests.Remove(msg.VesselId);
            if (before != msg.OwnerClientId || msg.Reason == AuthorityReason.Denied)
                Log.Info("Authority for " + remote.Label + ": " + (msg.OwnerClientId == 0 ? "nobody" : msg.OwnerClientId == Net.ClientId ? "us" : "#" + msg.OwnerClientId) + " (" + msg.Reason + ")");
            Registry.SyncReplica(remote);
            var kspVessel = FlightGlobals.fetch != null ? FlightGlobals.FindVessel(msg.VesselId) : null;
            if (kspVessel != null && msg.OwnerClientId == Net.ClientId) VesselImmortal.Set(kspVessel, false);
            UpdateSpectating();
        }

        private void OnFlightReady()
        {
            var active = FlightGlobals.ActiveVessel;
            if (active != null) Request(active.id);
            UpdateSpectating();
        }

        private void OnVesselChange(Vessel vessel)
        {
            if (vessel != null) Request(vessel.id);
            UpdateSpectating();
        }

        private void OnVesselUnloaded(Vessel vessel)
        {
            if (vessel == null || vessel.isActiveVessel) return;
            if (Registry.IsMine(vessel.id)) Release(vessel.id);
        }

        private void UpdateSpectating()
        {
            var active = FlightGlobals.ActiveVessel;
            SetSpectating(HighLogic.LoadedSceneIsFlight && active != null && Registry.IsOwnedByOther(active.id) && !Addon.Control.IAmAboard(active.id));
        }

        private void SetSpectating(bool spectating)
        {
            if (spectating == _spectating) return;
            _spectating = spectating;
            if (spectating)
            {
                InputLockManager.SetControlLock(ControlTypes.ALL_SHIP_CONTROLS, SpectateLockId);
                Log.Info("Spectating: the active vessel is simulated by " + SpectatingOwnerName);
            }
            else
            {
                InputLockManager.RemoveControlLock(SpectateLockId);
            }
        }
    }
}
