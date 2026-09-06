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
        private readonly List<Vessel> _stillNew = new List<Vessel>();
        private readonly List<Vessel> _splitOff = new List<Vessel>();
        private float _splitOffUntil = -1f;
        private readonly Dictionary<Guid, float> _newSince = new Dictionary<Guid, float>();
        /// <summary>How long a new vessel may keep us waiting for a valid orbit before it is announced anyway.</summary>
        public const float AnnounceGraceSeconds = 3f;
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
            // A kerbal leaving or boarding does not fire onVesselWasModified, so without this the vessel they
            // left keeps them aboard until the next periodic snapshot - up to 30 seconds of one kerbal in two
            // places at once.
            GameEvents.onVesselCrewWasModified.Add(OnVesselWasModified);
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
            GameEvents.onVesselCrewWasModified.Remove(OnVesselWasModified);
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(OnVesselTerminated);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            _modifiedAt.Clear();
            _newVessels.Clear();
            _idsMadeUnique.Clear();
            Registry.Clear();
        }

        // ---- receiving ----

        private void OnVesselProto(NetDataReader body)
        {
            var msg = Envelope.Read<VesselProtoMsg>(body);
            if (msg.VesselId == Guid.Empty) return;
            var remote = Registry.GetOrAdd(msg.VesselId);
            Registry.ApplyOwner(remote, msg.OwnerClientId, msg.AuthoritySeq, "a snapshot");
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
            if (OrbitIsMissing(proto))
            {
                // Announced before KSP had given it an orbit - a kerbal stepping out of a hatch does that. The
                // owner's state stream carries a good one within half a second, so use that rather than let the
                // loader reject the vessel, which is how a kerbal on EVA stayed invisible until its owner left.
                if (remote.HasState)
                {
                    FillOrbitFrom(proto, remote.LastState);
                    Log.Info("Snapshot of " + remote.Label + " has no orbit; took the one from its latest state");
                }
                else
                {
                    if (remote.NextApplyAt <= 0f) Log.Info("Snapshot of " + remote.Label + " has no orbit yet; waiting for a state");
                    remote.NextApplyAt = Time.realtimeSinceStartup + 0.5f;
                    return;
                }
            }
            remote.NextApplyAt = 0f;
            // A vessel we sit in but do not simulate is still refreshed when its parts change - see VesselLoader.
            var outcome = VesselLoader.Load(proto, false, !Registry.IsMine(remote));
            remote.ProtoDirty = outcome == VesselLoader.Outcome.Deferred;
            if (outcome == VesselLoader.Outcome.Loaded || outcome == VesselLoader.Outcome.Reloaded) Applied++;
            Registry.SyncReplica(remote);
        }

        /// <summary>Applies snapshots that arrived while the game was not ready (a few per frame).</summary>
        private void ApplyPending()
        {
            if (!VesselLoader.GameReady) return;
            var budget = MaxLoadsPerFrame;
            var now = Time.realtimeSinceStartup;
            foreach (var remote in Registry.All)
            {
                if (budget == 0) break;
                if (!remote.ProtoDirty || Registry.IsMine(remote) || remote.NextApplyAt > now) continue;
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

        private readonly HashSet<Guid> _idsMadeUnique = new HashSet<Guid>();

        /// <summary>
        /// A launched vessel keeps the persistent ids written in its .craft file, so two players launching the same
        /// craft end up with identical ids. KSP treats a colliding persistent id as the same object and destroys one
        /// of the two vessels, which is easy to hit because everyone has the same stock craft. Give every vessel we
        /// launch fresh ids before anyone else hears about it.
        /// </summary>
        private void EnsureUniquePersistentIds(Vessel vessel)
        {
            if (vessel == null || !_idsMadeUnique.Add(vessel.id)) return;
            try
            {
                var before = vessel.persistentId;
                vessel.persistentId = FlightGlobals.GetUniquepersistentId();
                if (vessel.parts != null)
                    for (var i = 0; i < vessel.parts.Count; i++)
                        vessel.parts[i].persistentId = FlightGlobals.GetUniquepersistentId();
                if (before != vessel.persistentId)
                    Log.Info("Gave " + vessel.GetDisplayName() + " fresh persistent ids (" + before + " -> " + vessel.persistentId + ") so it cannot collide with the same craft launched elsewhere");
            }
            catch (Exception e)
            {
                Log.Exception("Refreshing persistent ids for " + vessel.GetDisplayName(), e);
            }
        }

        public void SendProto(Vessel vessel, ProtoReason reason)
        {
            if (vessel == null || vessel.id == Guid.Empty || !Net.IsConnected) return;
            if (reason == ProtoReason.FlightReady || reason == ProtoReason.Created) EnsureUniquePersistentIds(vessel);
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
                    Name = vessel.GetDisplayName(),
                    VesselType = vessel.vesselType.ToString(),
                    ProtoDeflated = bytes,
                }, Channel.Bulk, Delivery.ReliableOrdered);
                Sent++;
                var remote = Registry.GetOrAdd(vessel.id);
                remote.Name = vessel.GetDisplayName();
                remote.PersistentId = vessel.persistentId;
                remote.LastProtoSentAt = Time.realtimeSinceStartup;
                if (remote.OwnerClientId == 0) remote.OwnerClientId = Net.ClientId; // the server confirms with AuthorityAssign
                if (reason != ProtoReason.Periodic) Log.Info("Sent snapshot of " + remote.Label + " (" + reason + ", " + bytes.Length + " bytes)");
            }
            catch (Exception e)
            {
                Log.Exception("Sending snapshot of " + vessel.GetDisplayName(), e);
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
            DiscardSplitOff();

            if (_newVessels.Count > 0)
            {
                _stillNew.Clear();
                foreach (var vessel in _newVessels)
                {
                    if (vessel == null || vessel.id == Guid.Empty || Registry.IsKnown(vessel.id) || Registry.IsTombstoned(vessel.id) || !vessel.loaded) continue;
                    if (!ReadyToAnnounce(vessel, now)) { _stillNew.Add(vessel); continue; }
                    _newSince.Remove(vessel.id);
                    Log.Info("New local vessel " + vessel.GetDisplayName() + ": claiming it");
                    Addon.Authority.Request(vessel.id);
                    SendProto(vessel, ProtoReason.Created);
                }
                _newVessels.Clear();
                _newVessels.AddRange(_stillNew);
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
            if (Time.realtimeSinceStartup < _splitOffUntil)
            {
                Registry.Tombstone(vessel.id);
                _splitOff.Add(vessel);
                return;
            }
            _newVessels.Add(vessel);
        }

        /// <summary>
        /// We are about to mirror a stage, a decoupler or a jettison on a vessel somebody else simulates, and
        /// KSP will split pieces off our copy of it. Those pieces are not ours to announce: the owner's real ones
        /// arrive as their own snapshots, and claiming ours would put every spent stage in the world twice. For
        /// the next moment, anything KSP creates locally is discarded instead.
        /// </summary>
        public void ExpectSplitOff(float seconds) => _splitOffUntil = Time.realtimeSinceStartup + seconds;

        private void DiscardSplitOff()
        {
            if (_splitOff.Count == 0) return;
            foreach (var vessel in _splitOff)
            {
                if (vessel == null) continue;
                if (vessel.isActiveVessel) { Log.Warn("Not discarding " + vessel.GetDisplayName() + ": it became the active vessel"); continue; }
                Log.Info("Discarding " + vessel.GetDisplayName() + ": it split off a vessel someone else simulates, and their copy of it arrives as its own snapshot");
                VesselLoader.Discard(vessel);
            }
            _splitOff.Clear();
        }

        /// <summary>
        /// A vessel is announced with a snapshot, and a snapshot taken the frame a vessel is born is not worth
        /// having. A kerbal stepping out of a hatch is a vessel whose orbit KSP has not computed yet, so the
        /// snapshot said NaN where the orbit should be, and everyone else's loader rejected it as invalid - and
        /// then never saw that kerbal until its owner left the flight. Wait for the orbit, and for a kerbal's
        /// controller to say it is ready, but not forever: a vessel that never gets there is still announced.
        /// </summary>
        private bool ReadyToAnnounce(Vessel vessel, float now)
        {
            if (!_newSince.TryGetValue(vessel.id, out var since)) _newSince[vessel.id] = since = now;
            if (now - since >= AnnounceGraceSeconds)
            {
                Log.Warn("Announcing " + vessel.GetDisplayName() + " although it never settled: orbit "
                         + (OrbitIsValid(vessel) ? "ok" : "invalid") + (vessel.isEVA ? ", kerbal " + (KerbalReady(vessel) ? "ready" : "not ready") : ""));
                return true;
            }
            return OrbitIsValid(vessel) && KerbalReady(vessel);
        }

        private static bool OrbitIsMissing(ProtoVessel proto)
        {
            var o = proto.orbitSnapShot;
            return o == null || double.IsNaN(o.semiMajorAxis) || double.IsNaN(o.eccentricity) || double.IsNaN(o.inclination)
                   || double.IsNaN(o.meanAnomalyAtEpoch) || double.IsNaN(o.epoch) || double.IsInfinity(o.semiMajorAxis);
        }

        private static void FillOrbitFrom(ProtoVessel proto, VesselStateMsg state)
        {
            var bodies = FlightGlobals.Bodies;
            var body = bodies != null && state.BodyIndex < bodies.Count ? bodies[state.BodyIndex] : null;
            if (body == null) return;
            var orbit = new Orbit();
            orbit.SetOrbit(state.Inclination, state.Eccentricity, state.SemiMajorAxis, state.Lan, state.ArgumentOfPeriapsis, state.MeanAnomalyAtEpoch, state.Epoch, body);
            proto.orbitSnapShot = new OrbitSnapshot(orbit);
        }

        private static bool OrbitIsValid(Vessel vessel)
        {
            var orbit = vessel.orbitDriver != null ? vessel.orbitDriver.orbit : null;
            return orbit != null && orbit.referenceBody != null
                   && !double.IsNaN(orbit.semiMajorAxis) && !double.IsNaN(orbit.eccentricity) && !double.IsNaN(orbit.inclination)
                   && !double.IsNaN(orbit.meanAnomalyAtEpoch) && !double.IsNaN(orbit.epoch) && !double.IsInfinity(orbit.semiMajorAxis);
        }

        private static bool KerbalReady(Vessel vessel) => !vessel.isEVA || vessel.evaController == null || vessel.evaController.Ready;

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
