using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using KspMp.Vessels;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Docking. Two vessels can only dock when one client simulates both, so when our vessel gets within
    /// <see cref="ApproachMeters"/> of a vessel somebody else owns we ask the server to move it under one owner.
    /// The owner reports the finished docking with the merged snapshot; everyone else reloads the survivor and
    /// drops the vessel that was absorbed.
    /// </summary>
    public sealed class DockSystem : SystemBase
    {
        public const float ApproachMeters = 50f;
        private const float IntentIntervalSeconds = 5f;

        private readonly Dictionary<Guid, float> _intentSentAt = new Dictionary<Guid, float>();
        private readonly List<Guid> _lastCoupleVessels = new List<Guid>();
        private float _nextScanAt;

        public DockSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Dock";
        private VesselRegistry Registry => Addon.Vessels;
        public int Commits { get; private set; }

        public override bool ShouldRun(GameScenes scene, bool connected) => connected && scene == GameScenes.FLIGHT;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.DockCommit, OnDockCommit);
            GameEvents.onPartCouple.Add(OnPartCouple);
            GameEvents.onDockingComplete.Add(OnDockingComplete);
            GameEvents.onPartCoupleComplete.Add(OnDockingComplete);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.DockCommit, OnDockCommit);
            GameEvents.onPartCouple.Remove(OnPartCouple);
            GameEvents.onDockingComplete.Remove(OnDockingComplete);
            GameEvents.onPartCoupleComplete.Remove(OnDockingComplete);
            _intentSentAt.Clear();
            _lastCoupleVessels.Clear();
        }

        public override void Update()
        {
            if (!FlightGlobals.ready) return;
            var now = Time.realtimeSinceStartup;
            if (now < _nextScanAt) return;
            _nextScanAt = now + 1f;

            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var mine = loaded[i];
                if (mine == null || !Registry.IsMine(mine.id)) continue;
                for (var j = 0; j < loaded.Count; j++)
                {
                    var other = loaded[j];
                    if (other == null || other == mine || !Registry.IsOwnedByOther(other.id)) continue;
                    var distance = (float)(mine.GetWorldPos3D() - other.GetWorldPos3D()).magnitude;
                    if (distance < ApproachMeters) SendIntent(mine.id, other.id, distance);
                }
            }
        }

        public void SendIntent(Guid mine, Guid other, float distance)
        {
            var now = Time.realtimeSinceStartup;
            if (_intentSentAt.TryGetValue(other, out var at) && now - at < IntentIntervalSeconds) return;
            _intentSentAt[other] = now;
            Net.Send(MessageId.DockIntent, new DockIntentMsg { MyVesselId = mine, OtherVesselId = other, DistanceMeters = distance }, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Docking approach: asked the server to put " + (Registry.TryGet(other, out var rv) ? rv.Label : other.ToString()) + " under one physics owner (" + distance.ToString("F0") + " m)");
        }

        private void OnPartCouple(GameEvents.FromToAction<Part, Part> action)
        {
            if (action.from == null || action.to == null || action.from.vessel == null || action.to.vessel == null) return;
            if (VesselLoader.IsLoadingRemote) return;
            _lastCoupleVessels.Clear();
            _lastCoupleVessels.Add(action.from.vessel.id);
            _lastCoupleVessels.Add(action.to.vessel.id);
        }

        private void OnDockingComplete(GameEvents.FromToAction<Part, Part> action)
        {
            if (VesselLoader.IsLoadingRemote || action.from == null || action.from.vessel == null) return;
            var survivor = action.from.vessel;
            if (_lastCoupleVessels.Count != 2) return;
            var removed = _lastCoupleVessels[0] == survivor.id ? _lastCoupleVessels[1] : _lastCoupleVessels[0];
            _lastCoupleVessels.Clear();
            if (removed == survivor.id || !Registry.IsMine(survivor.id) && !Registry.IsMine(removed)) return;

            try
            {
                var bytes = ProtoCodec.Serialize(survivor.BackupVessel());
                Net.Send(MessageId.DockCommit, new DockCommitMsg
                {
                    SurvivorVesselId = survivor.id,
                    RemovedVesselId = removed,
                    OwnerClientId = Net.ClientId,
                    Name = survivor.GetDisplayName(),
                    ProtoDeflated = bytes,
                }, Channel.Bulk, Delivery.ReliableOrdered);
                Registry.Remove(removed);
                Registry.Tombstone(removed);
                var rv = Registry.GetOrAdd(survivor.id);
                rv.OwnerClientId = Net.ClientId;
                rv.Name = survivor.GetDisplayName();
                Commits++;
                Log.Info("Docked: " + removed.ToString().Substring(0, 8) + " merged into " + rv.Label + " (" + bytes.Length + " bytes)");
            }
            catch (Exception e)
            {
                Log.Exception("Reporting docking", e);
            }
        }

        private void OnDockCommit(NetDataReader body)
        {
            var msg = Envelope.Read<DockCommitMsg>(body);
            if (msg.OwnerClientId == Net.ClientId) return;
            var survivor = Registry.GetOrAdd(msg.SurvivorVesselId);
            survivor.OwnerClientId = msg.OwnerClientId;
            survivor.Name = msg.Name;
            survivor.ProtoDeflated = msg.ProtoDeflated;
            survivor.ProtoDirty = true;
            Log.Info("Docking by #" + msg.OwnerClientId + ": " + msg.RemovedVesselId.ToString().Substring(0, 8) + " merged into " + survivor.Label);

            var active = FlightGlobals.ActiveVessel;
            var wasOurActive = active != null && active.id == msg.RemovedVesselId;
            var survivorWasActive = active != null && active.id == msg.SurvivorVesselId;
            try
            {
                var proto = ProtoCodec.ToProto(msg.ProtoDeflated, HighLogic.CurrentGame);
                if (proto != null)
                {
                    var outcome = VesselLoader.Load(proto, true, survivorWasActive || wasOurActive);
                    survivor.ProtoDirty = outcome == VesselLoader.Outcome.Deferred;
                }
                if (wasOurActive)
                {
                    var survivorVessel = FlightGlobals.FindVessel(msg.SurvivorVesselId);
                    if (survivorVessel != null)
                    {
                        Log.Info("Our vessel was absorbed; switching to " + survivorVessel.GetDisplayName());
                        FlightGlobals.ForceSetActiveVessel(survivorVessel);
                    }
                }
                Registry.Remove(msg.RemovedVesselId);
                Registry.Tombstone(msg.RemovedVesselId);
                VesselLoader.Remove(msg.RemovedVesselId, "docked");
                Registry.SyncReplica(survivor);
            }
            catch (Exception e)
            {
                Log.Exception("Applying docking", e);
            }
        }
    }
}
