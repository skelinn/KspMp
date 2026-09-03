using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using KspMp.Vessels;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Full vessel snapshots. Owners send them on flight ready, after modifications, when new vessels appear, when
    /// leaving flight and every 30 s; everyone else loads them into their game. Also receives removals.
    /// </summary>
    public sealed class VesselProtoSystem : SystemBase
    {
        public const float ModifiedDebounceSeconds = 0.5f;
        public const float PeriodicSeconds = 30f;
        private const int MaxLoadsPerFrame = 2;

        private readonly Dictionary<Guid, float> _modifiedAt = new Dictionary<Guid, float>();
        private readonly List<Vessel> _newVessels = new List<Vessel>();
        private bool _sceneChanging;
        private bool _loggedSample;

        public VesselProtoSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "VesselProto";
        private VesselRegistry Registry => Addon.Vessels;
        public int Sent { get; private set; }
        public int Applied { get; private set; }

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.VesselProto, OnVesselProto);
            Net.RegisterHandler(MessageId.VesselRemove, OnVesselRemove);
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
            GameEvents.onVesselTerminated.Add(OnVesselTerminated);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.VesselProto, OnVesselProto);
            Net.UnregisterHandler(MessageId.VesselRemove, OnVesselRemove);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(OnVesselTerminated);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            _modifiedAt.Clear();
            _newVessels.Clear();
            Registry.Clear();
        }

        // ---- receiving ----

        private void OnVesselProto(NetDataReader body)
        {
            var msg = Envelope.Read<VesselProtoMsg>(body);
            if (msg.VesselId == Guid.Empty) return;
            var remote = Registry.GetOrAdd(msg.VesselId);
            remote.OwnerClientId = msg.OwnerClientId;
            remote.PersistentId = msg.PersistentId;
            remote.Name = msg.Name;
            remote.VesselType = msg.VesselType;
            remote.ProtoDeflated = msg.ProtoDeflated;
            if (Registry.IsMine(remote) || Registry.IsTombstoned(msg.VesselId))
            {
                remote.ProtoDirty = false;
                return;
            }
            remote.ProtoDirty = true;
            if (msg.Reason != ProtoReason.Periodic) Log.Info("Snapshot of " + remote.Label + " from #" + msg.OwnerClientId + " (" + msg.Reason + ", " + (msg.ProtoDeflated != null ? msg.ProtoDeflated.Length : 0) + " bytes)");
            TryApply(remote);
        }

        private void OnVesselRemove(NetDataReader body)
        {
            var msg = Envelope.Read<VesselRemoveMsg>(body);
            Registry.Remove(msg.VesselId);
            Registry.Tombstone(msg.VesselId);
            VesselLoader.Remove(msg.VesselId, msg.Reason);
        }

        private void TryApply(RemoteVessel remote)
        {
            if (!remote.ProtoDirty || !VesselLoader.GameReady) return;
            ProtoVessel proto;
            try
            {
                proto = ProtoCodec.ToProto(remote.ProtoDeflated, HighLogic.CurrentGame);
            }
            catch (Exception e)
            {
                Log.Exception("Parsing snapshot of " + remote.Label, e);
                remote.ProtoDirty = false;
                return;
            }
            if (proto == null)
            {
                Log.Warn("Snapshot of " + remote.Label + " is empty");
                remote.ProtoDirty = false;
                return;
            }
            var outcome = VesselLoader.Load(proto, false);
            remote.ProtoDirty = outcome == VesselLoader.Outcome.Deferred;
            if (outcome == VesselLoader.Outcome.Loaded || outcome == VesselLoader.Outcome.Reloaded) Applied++;
            Registry.SyncReplica(remote);
        }

        /// <summary>Applies snapshots that arrived while the game was not ready (a few per frame).</summary>
        private void ApplyPending()
        {
            if (!VesselLoader.GameReady) return;
            var budget = MaxLoadsPerFrame;
            foreach (var remote in Registry.All)
            {
                if (budget == 0) break;
                if (!remote.ProtoDirty || Registry.IsMine(remote)) continue;
                TryApply(remote);
                budget--;
            }
        }

        /// <summary>Before entering the game from the lobby: put every known vessel into the new save's flight state.</summary>
        public int SeedFlightState(global::Game game)
        {
            var added = 0;
            foreach (var remote in Registry.All)
            {
                if (remote.ProtoDeflated == null) continue;
                try
                {
                    var proto = ProtoCodec.ToProto(remote.ProtoDeflated, game);
                    if (proto == null) continue;
                    game.flightState.protoVessels.Add(proto);
                    remote.ProtoDirty = false;
                    added++;
                }
                catch (Exception e)
                {
                    Log.Exception("Seeding " + remote.Label, e);
                }
            }
            return added;
        }

        // ---- sending ----

        public void SendProto(Vessel vessel, ProtoReason reason)
        {
            if (vessel == null || vessel.id == Guid.Empty || !Net.IsConnected) return;
            try
            {
                var proto = vessel.BackupVessel();
                var bytes = ProtoCodec.Serialize(proto);
                if (!_loggedSample)
                {
                    _loggedSample = true;
                    var text = System.Text.Encoding.UTF8.GetString(KspMp.Shared.Codec.DeflateCodec.Decompress(bytes, 0, bytes.Length));
                    Log.Info("First snapshot text starts with: " + text.Substring(0, Math.Min(80, text.Length)).Replace("\n", "\\n"));
                }
                Net.Send(MessageId.VesselProto, new VesselProtoMsg
                {
                    VesselId = vessel.id,
                    PersistentId = vessel.persistentId,
                    OwnerClientId = Net.ClientId,
                    Reason = reason,
                    Name = vessel.vesselName,
                    VesselType = vessel.vesselType.ToString(),
                    ProtoDeflated = bytes,
                }, Channel.Bulk, Delivery.ReliableOrdered);
                Sent++;
                var remote = Registry.GetOrAdd(vessel.id);
                remote.Name = vessel.vesselName;
                remote.PersistentId = vessel.persistentId;
                remote.LastProtoSentAt = Time.realtimeSinceStartup;
                if (remote.OwnerClientId == 0) remote.OwnerClientId = Net.ClientId; // the server confirms with AuthorityAssign
                if (reason != ProtoReason.Periodic) Log.Info("Sent snapshot of " + remote.Label + " (" + reason + ", " + bytes.Length + " bytes)");
            }
            catch (Exception e)
            {
                Log.Exception("Sending snapshot of " + vessel.vesselName, e);
            }
        }

        public void SendRemove(Guid vesselId, string why)
        {
            if (vesselId == Guid.Empty || !Net.IsConnected) return;
            Log.Info("Telling the server vessel " + vesselId.ToString().Substring(0, 8) + " is gone (" + why + ")");
            Net.Send(MessageId.VesselRemove, new VesselRemoveMsg { VesselId = vesselId, Reason = why }, Channel.Bulk, Delivery.ReliableOrdered);
            Registry.Remove(vesselId);
            Registry.Tombstone(vesselId);
        }

        public override void Update()
        {
            ApplyPending();
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            var now = Time.realtimeSinceStartup;

            if (_newVessels.Count > 0)
            {
                foreach (var vessel in _newVessels)
                {
                    if (vessel == null || vessel.id == Guid.Empty || Registry.IsKnown(vessel.id) || Registry.IsTombstoned(vessel.id) || !vessel.loaded) continue;
                    Log.Info("New local vessel " + vessel.vesselName + ": claiming it");
                    Addon.Authority.Request(vessel.id);
                    SendProto(vessel, ProtoReason.Created);
                }
                _newVessels.Clear();
            }

            if (_modifiedAt.Count > 0)
            {
                var due = new List<Guid>();
                foreach (var pair in _modifiedAt)
                    if (now - pair.Value >= ModifiedDebounceSeconds) due.Add(pair.Key);
                foreach (var id in due)
                {
                    _modifiedAt.Remove(id);
                    var vessel = FlightGlobals.FindVessel(id);
                    if (vessel != null && Registry.IsMine(id)) SendProto(vessel, ProtoReason.Modified);
                }
            }

            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel == null || !Registry.TryGet(vessel.id, out var remote) || !Registry.IsMine(remote)) continue;
                if (now - remote.LastProtoSentAt >= PeriodicSeconds) SendProto(vessel, ProtoReason.Periodic);
            }
        }

        // ---- game events ----

        private void OnFlightReady()
        {
            var active = FlightGlobals.ActiveVessel;
            if (active == null) return;
            if (Registry.IsOwnedByOther(active.id)) return;
            SendProto(active, ProtoReason.FlightReady);
        }

        private void OnVesselWasModified(Vessel vessel)
        {
            if (vessel == null || !HighLogic.LoadedSceneIsFlight) return;
            if (Registry.IsMine(vessel.id)) _modifiedAt[vessel.id] = Time.realtimeSinceStartup;
        }

        private void OnVesselGoOnRails(Vessel vessel)
        {
            if (vessel == null || !Registry.IsMine(vessel.id) || !vessel.LandedOrSplashed) return;
            SendProto(vessel, ProtoReason.OnRails);
        }

        private void OnVesselCreate(Vessel vessel)
        {
            if (vessel == null || VesselLoader.IsLoadingRemote || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            _newVessels.Add(vessel);
        }

        private void OnVesselWillDestroy(Vessel vessel)
        {
            if (vessel == null || _sceneChanging || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            if (Registry.IsMine(vessel.id)) SendRemove(vessel.id, "destroyed");
        }

        private void OnVesselRecovered(ProtoVessel proto, bool quick)
        {
            if (proto != null) SendRemove(proto.vesselID, "recovered");
        }

        private void OnVesselTerminated(ProtoVessel proto)
        {
            if (proto != null) SendRemove(proto.vesselID, "terminated");
        }

        private void OnSceneLoadRequested(GameScenes scene)
        {
            _sceneChanging = true;
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null) return;
            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel != null && Registry.IsMine(vessel.id)) SendProto(vessel, ProtoReason.LeavingFlight);
            }
            Addon.Authority.ReleaseAll("leaving flight");
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            _sceneChanging = false;
            ApplyPending();
        }
    }
}
