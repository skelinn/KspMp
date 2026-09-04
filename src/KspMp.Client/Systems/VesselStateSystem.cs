using System;
using KspMp.Shared.Protocol;
using KspMp.Vessels;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>Streams the state of vessels we simulate and drives replicas of everyone else's. Flight scene only.</summary>
    public sealed class VesselStateSystem : SystemBase
    {
        public const float ActiveIntervalSeconds = 0.1f;
        public const float OtherIntervalSeconds = 0.5f;
        private const TimingManager.TimingStage ApplyStage = TimingManager.TimingStage.BetterLateThanNever;

        public VesselStateSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "VesselState";
        private VesselRegistry Registry => Addon.Vessels;
        public int Sent { get; private set; }
        public int Received { get; private set; }

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.VesselState, OnVesselState);
            TimingManager.FixedUpdateAdd(ApplyStage, ApplyReplicas);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.VesselState, OnVesselState);
            TimingManager.FixedUpdateRemove(ApplyStage, ApplyReplicas);
        }

        private float _nextStatsAt;

        public override void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || !Net.IsConnected) return;
            var now = Time.realtimeSinceStartup;
            var ut = Planetarium.GetUniversalTime();
            if (now >= _nextStatsAt)
            {
                _nextStatsAt = now + 10f;
                LogStats(ut);
            }
            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel == null || vessel.id == Guid.Empty || !Registry.TryGet(vessel.id, out var remote) || !Registry.IsMine(remote)) continue;
                var interval = vessel.isActiveVessel ? ActiveIntervalSeconds : OtherIntervalSeconds;
                if (now - remote.LastStateSentAt < interval) continue;
                remote.LastStateSentAt = now;
                try
                {
                    Net.Send(MessageId.VesselState, VesselStateCapture.Capture(vessel, ut), Channel.State, Delivery.Sequenced);
                    Sent++;
                }
                catch (Exception e)
                {
                    Log.Exception("Capturing state of " + vessel.vesselName, e);
                }
            }
        }

        private void LogStats(double ut)
        {
            var active = FlightGlobals.ActiveVessel;
            var line = "Vessel sync (warp " + TimeWarp.CurrentRate + "x): " + Registry.Count + " known, " + Registry.CountOwnedByMe + " ours, " + Registry.CountReplicas + " replicas; states sent " + Sent + " recv " + Received
                       + (active != null ? "; ours " + active.GetDisplayName() + " alt " + active.altitude.ToString("F0") + " " + active.situation : "");
            foreach (var remote in Registry.All)
            {
                if (remote.Replica == null) continue;
                var vessel = remote.Replica.Vessel;
                line += "; replica " + remote.Label + (vessel != null ? (vessel.loaded ? " loaded" : " unloaded") + (vessel.packed ? " packed" : "") + " alt " + vessel.altitude.ToString("F0") : " (no vessel)")
                        + " state age " + (ut - remote.Replica.LastUt).ToString("F1") + " s applied " + remote.Replica.Applied;
            }
            Log.Info(line);
        }

        private void OnVesselState(NetDataReader body)
        {
            var msg = Envelope.Read<VesselStateMsg>(body);
            if (Registry.IsTombstoned(msg.VesselId)) return;
            var remote = Registry.GetOrAdd(msg.VesselId);
            if (Registry.IsMine(remote)) return;
            remote.HasState = true;
            remote.LastState = msg;
            Received++;
            if (remote.Replica == null) Registry.SyncReplica(remote);
            remote.Replica?.Push(msg);
        }

        private void ApplyReplicas()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            var ut = Planetarium.GetUniversalTime();
            foreach (var remote in Registry.All)
            {
                if (remote.Replica == null) continue;
                try
                {
                    remote.Replica.Apply(ut);
                }
                catch (Exception e)
                {
                    Log.Exception("Positioning replica " + remote.Label, e);
                }
            }
        }
    }
}
