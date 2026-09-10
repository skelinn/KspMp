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
        /// <summary>
        /// Pieces that came off a vessel somebody else simulates, on this machine, waiting for the owner's own
        /// snapshot of them. They used to be destroyed on the spot and the owner's copy loaded a moment later,
        /// so a booster visibly separated, vanished, and reappeared somewhere else - "decoupling felt buggy".
        /// Kept, frozen, they are adopted as the owner's vessel when its snapshot names the same parts.
        /// </summary>
        private sealed class Phantom { public Vessel Vessel; public float At; }
        private readonly List<Phantom> _phantoms = new List<Phantom>();
        public const float PhantomSeconds = 3f;
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
            GameEvents.onVesselsUndocking.Add(OnSplit);
            GameEvents.onPartDeCoupleNewVesselComplete.Add(OnSplit);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
            GameEvents.OnVesselRecoveryRequested.Add(OnRecoveryRequested);
            GameEvents.onVesselTerminated.Add(OnVesselTerminated);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
            if (Addon.SyncedOnce && Registry.Count == 0 && Net.IsConnected)
            {
                // Re-activated with an empty registry (a scene the mod stays out of wiped it): ask for the world
                // again, or only the vessels whose owners are in flight would ever reappear.
                Log.Info("The registry is empty; asking the server for the world again");
                Net.Send(MessageId.SyncRequest, new SyncRequestMsg(), Channel.Control, Delivery.ReliableOrdered);
            }
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
            GameEvents.onVesselsUndocking.Remove(OnSplit);
            GameEvents.onPartDeCoupleNewVesselComplete.Remove(OnSplit);
            _splitParent.Clear();
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            GameEvents.OnVesselRecoveryRequested.Remove(OnRecoveryRequested);
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
            if (Registry.IsMine(remote))
            {
                remote.ProtoDirty = false;
                return;
            }
            if (Registry.IsTombstoned(msg.VesselId))
            {
                // Just removed here. Not thrown away: a docking commit, a revert or a re-sync can remove and
                // re-announce a vessel within the tombstone window, and dropping the snapshot left the vessel
                // missing from the world until the owner's next periodic one, up to thirty seconds later.
                remote.ProtoDirty = true;
                remote.NextApplyAt = Time.realtimeSinceStartup + VesselRegistry.TombstoneSeconds;
                return;
            }
            remote.ProtoDirty = true;
            if (Time.realtimeSinceStartup < _forceUntil) remote.ForceReload = true;
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
            if (OrbitIsMissing(proto) || remote.OrbitFromState)
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
            var active = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (remote.OwnerClientId == 0 && active != null && active.id == remote.Id && Net.IsConnected)
            {
                // Our own ship, back from the server with no owner: we reconnected mid-flight. The server's
                // copy is up to thirty seconds stale; rebuilding the rocket under us from it rewound the
                // ascent. We are the truth here: claim it and send it.
                Log.Info("Snapshot of " + remote.Label + " is the vessel we are flying, unowned: claiming it back rather than reloading it");
                remote.ProtoDirty = false;
                Addon.Authority.Request(remote.Id);
                SendProto(active, ProtoReason.Modified);
                return;
            }
            if (FlightGlobals.FindVessel(proto.vesselID) == null && TryAdoptPhantom(proto, remote)) return;
            // A vessel we sit in but do not simulate is still refreshed when its parts change - see VesselLoader.
            var force = remote.ForceReload;
            remote.ForceReload = false;
            var outcome = VesselLoader.Load(proto, force, !Registry.IsMine(remote));
            if (outcome == VesselLoader.Outcome.InvalidOrbit)
            {
                // Born this frame on the owner's machine; its state stream carries a real orbit within a
                // tenth of a second. Try again with that rather than give up until the next snapshot.
                remote.ProtoDirty = true;
                remote.OrbitFromState = true;
                remote.NextApplyAt = Time.realtimeSinceStartup + 0.25f;
                return;
            }
            remote.OrbitFromState = false;
            remote.ProtoDirty = outcome == VesselLoader.Outcome.Deferred;
            if (outcome != VesselLoader.Outcome.Deferred && outcome != VesselLoader.Outcome.Failed && proto.protoPartSnapshots != null)
            {
                remote.PartIds = remote.PartIds ?? new HashSet<uint>();
                remote.PartIds.Clear();
                foreach (var part in proto.protoPartSnapshots) remote.PartIds.Add(part.flightID);
            }
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
                    // Left dirty on purpose: if KSP drops the proto while starting the game (a part it cannot
                    // build), the first ApplyPending loads it again; if it took, that pass finds it unchanged.
                    remote.ProtoDirty = true;
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
        /// New vessel id -> the vessel it came off. An undocked or decoupled ship sits within docking-approach
        /// range of what it left, and the approach rule would hand one of them straight back to the other
        /// player; the server needs to know the pair just separated to leave them alone for a while.
        /// </summary>
        private readonly Dictionary<Guid, Guid> _splitParent = new Dictionary<Guid, Guid>();

        private void OnSplit(Vessel from, Vessel to)
        {
            if (from == null || to == null || from == to) return;
            // KSP passes (old, new) for a decouple; for an undock either may be the one that kept the id.
            if (Registry.IsKnown(from.id) && !Registry.IsKnown(to.id)) _splitParent[to.id] = from.id;
            else if (Registry.IsKnown(to.id) && !Registry.IsKnown(from.id)) _splitParent[from.id] = to.id;
            else _splitParent[to.id] = from.id;
        }

        /// <summary>Vessels this flight claimed as new since it became ready: what a revert makes vanish.</summary>
        private readonly List<Guid> _createdThisFlight = new List<Guid>();

        /// <summary>
        /// The vessel a revert to launch brings straight back under the same id. The scene reload that follows
        /// looks like leaving flight, and must not release it or snapshot its mid-flight state on the way out:
        /// the other players saw it change hands for a second and then reload, for nothing.
        /// </summary>
        private Guid _keepThroughRevert;

        /// <summary>
        /// The player is about to revert (see RevertGuard). Withdraw from the server everything this flight
        /// created, and the vessel itself unless the revert brings it back under the same id, and forget the
        /// persistent ids we made unique so the reloaded vessel gets fresh ones again.
        /// </summary>
        public void OnReverting(string what, bool vesselComesBack, Guid launched)
        {
            // 'launched' is the vessel the revert acts on (RevertGuard.LaunchedVesselId): the one this flight
            // began with, whatever the player has switched to since.
            var keep = vesselComesBack ? launched : Guid.Empty;
            var withdrawn = 0;
            for (var i = 0; i < _createdThisFlight.Count; i++)
            {
                var id = _createdThisFlight[i];
                if (id == keep) continue;
                // Landed debris has its authority released, so "still ours" is the wrong test: withdraw
                // everything the server still lists that nobody else has taken over. What somebody else
                // simulates exists in their world whatever ours reverts to.
                if (!Registry.IsKnown(id) || Registry.IsOwnedByOther(id)) continue;
                SendRemove(id, what.ToLowerInvariant());
                withdrawn++;
            }
            if (!vesselComesBack && launched != Guid.Empty && Registry.IsKnown(launched) && !Registry.IsOwnedByOther(launched) && !_createdThisFlight.Contains(launched))
            {
                SendRemove(launched, what.ToLowerInvariant());
                withdrawn++;
            }
            _createdThisFlight.Clear();
            if (launched != Guid.Empty) _idsMadeUnique.Remove(launched);
            _keepThroughRevert = vesselComesBack && launched != Guid.Empty && Registry.IsMine(launched) ? launched : Guid.Empty;

            // The revert reloads the world as KSP saved it at launch. Other players' vessels launched since
            // are not in that save, and their copies in it are stale; every snapshot we hold is applied again
            // once the scene is back (OnLevelLoaded -> ApplyPending), rather than waiting for the next
            // periodic one.
            var refreshed = 0;
            foreach (var remote in Registry.All)
            {
                if (Registry.IsMine(remote) || remote.ProtoDeflated == null) continue;
                remote.ProtoDirty = true;
                refreshed++;
            }
            Log.Info(what + ": withdrew " + withdrawn + " vessel(s) this flight had created, " + refreshed + " other snapshot(s) will be applied again"
                     + (vesselComesBack && launched != Guid.Empty ? "; " + launched.ToString().Substring(0, 8) + " keeps its id and comes back on the pad" : ""));
        }

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
                var splitFrom = Guid.Empty;
                if (reason == ProtoReason.Created && _splitParent.TryGetValue(vessel.id, out splitFrom))
                {
                    _splitParent.Remove(vessel.id);
                    Log.Info(vessel.GetDisplayName() + " separated from " + splitFrom.ToString().Substring(0, 8) + "; the server will hold the docking rule off for a while");
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
                    SplitFrom = splitFrom,
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
                    if (vessel == null || vessel.id == Guid.Empty || Registry.IsKnown(vessel.id)) continue;
                    if (Registry.WasRemoved(vessel.id))
                    {
                        // A revert reloads the world as it was at launch, which can include vessels that have
                        // since been removed for everyone (recovered, crashed, withdrawn by their own revert);
                        // and the copy of a recovered craft we rode to the space centre comes back from the
                        // save the same way. They are not new; claiming them would resurrect them on the
                        // server, and our Kerbal "aboard" one would invite us to fly it. Before the tombstone
                        // check: a fresh tombstone used to hide the zombie from this until it expired.
                        Log.Info("Discarding " + vessel.GetDisplayName() + ": it was removed earlier and only came back with a scene load");
                        VesselLoader.Discard(vessel);
                        continue;
                    }
                    if (Registry.IsTombstoned(vessel.id)) continue;
                    if (!vessel.loaded) { _stillNew.Add(vessel); continue; }   // ask again; dropping it meant nobody ever saw it
                    if (SplitOffSomebodyElses(vessel, out var from))
                    {
                        // Pieces of a vessel someone else simulates are never ours, whenever they came apart: the
                        // owner's copy of them arrives as its own snapshot. A timed window missed the ones that
                        // broke off later, and each of those became a duplicate vessel in the world.
                        Registry.Tombstone(vessel.id);
                        KeepPhantom(vessel, from);
                        continue;
                    }
                    if (!ReadyToAnnounce(vessel, now)) { _stillNew.Add(vessel); continue; }
                    _newSince.Remove(vessel.id);
                    Log.Info("New local vessel " + vessel.GetDisplayName() + ": claiming it");
                    Addon.Authority.Request(vessel.id);
                    SendProto(vessel, ProtoReason.Created);
                    _createdThisFlight.Add(vessel.id);
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
            _createdThisFlight.Clear();   // a new flight: what an earlier one created is not this one's to withdraw
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
                KeepPhantom(vessel, null);
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

        private float _forceUntil = -1f;
        private float _lastResyncAt = -100f;
        public const float ResyncCooldownSeconds = 10f;
        public bool ResyncAvailable => Time.realtimeSinceStartup - _lastResyncAt >= ResyncCooldownSeconds;

        /// <summary>
        /// The player's "something looks wrong" button: asks the server for the whole world again and rebuilds
        /// every other player's vessel from the snapshots that come back, whether or not they look changed.
        /// Our own vessels are not touched; the copy we sit in as a co-pilot is rebuilt like any other.
        /// </summary>
        public void Resync()
        {
            if (!ResyncAvailable || !Net.IsConnected) return;
            _lastResyncAt = Time.realtimeSinceStartup;
            _forceUntil = _lastResyncAt + 5f;
            Net.Send(MessageId.SyncRequest, new SyncRequestMsg(), Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Resync: asked the server for the world again; the other players' vessels will be rebuilt");
            Addon.Notices.Post("resync", "Asked the server for the world again; the other players' craft are being rebuilt", Ui.Theme.Ink, ttlSeconds: 8f);
        }

        private void KeepPhantom(Vessel vessel, RemoteVessel from)
        {
            if (vessel == null) return;
            for (var i = 0; i < _phantoms.Count; i++) if (_phantoms[i].Vessel == vessel) return;
            VesselImmortal.Set(vessel, true);   // it is somebody else's; it must not explode or fall apart here
            _phantoms.Add(new Phantom { Vessel = vessel, At = Time.realtimeSinceStartup });
            var name = vessel.GetDisplayName();
            if (string.IsNullOrEmpty(name)) name = "a piece";
            Log.Info("Keeping " + name + " (" + (vessel.parts != null ? vessel.parts.Count : 0) + " parts), which split off "
                     + (from != null ? from.Label + ", " + NameOf(from.OwnerClientId) + "'s" : "somebody else's vessel") + ", until their copy of it arrives");
        }

        /// <summary>Phantoms nobody claimed in time are not the owner's after all (or their copy failed): discard them.</summary>
        private void DiscardSplitOff()
        {
            if (_phantoms.Count == 0) return;
            var now = Time.realtimeSinceStartup;
            for (var i = _phantoms.Count - 1; i >= 0; i--)
            {
                var phantom = _phantoms[i];
                if (phantom.Vessel == null) { _phantoms.RemoveAt(i); continue; }
                if (now - phantom.At < PhantomSeconds) continue;
                _phantoms.RemoveAt(i);
                if (phantom.Vessel.isActiveVessel) { Log.Warn("Not discarding " + phantom.Vessel.GetDisplayName() + ": it became the active vessel"); continue; }
                Log.Info("Discarding " + phantom.Vessel.GetDisplayName() + ": it split off a vessel someone else simulates and their copy never named it");
                VesselLoader.Discard(phantom.Vessel);
            }
        }

        /// <summary>
        /// The owner's snapshot of a piece that already exists here as a phantom: make the phantom that vessel
        /// instead of loading a second copy over it. Part flight ids are the same on every machine, which is
        /// how the pieces were recognised as theirs in the first place.
        /// </summary>
        private bool TryAdoptPhantom(ProtoVessel proto, RemoteVessel remote)
        {
            if (_phantoms.Count == 0 || proto.protoPartSnapshots == null || proto.protoPartSnapshots.Count == 0) return false;
            var ids = new HashSet<uint>();
            foreach (var snap in proto.protoPartSnapshots) ids.Add(snap.flightID);
            for (var i = 0; i < _phantoms.Count; i++)
            {
                var vessel = _phantoms[i].Vessel;
                if (vessel == null || vessel.parts == null || vessel.parts.Count != ids.Count) continue;
                var same = true;
                for (var p = 0; p < vessel.parts.Count && same; p++)
                    same = vessel.parts[p] != null && ids.Contains(vessel.parts[p].flightID);
                if (!same) continue;
                _phantoms.RemoveAt(i);
                var oldId = vessel.id;
                vessel.id = proto.vesselID;
                vessel.vesselName = proto.vesselName;
                vessel.vesselType = proto.vesselType;
                vessel.protoVessel = proto;
                proto.vesselRef = vessel;
                remote.PartIds = ids;
                remote.ProtoDirty = false;
                remote.OrbitFromState = false;
                Registry.SyncReplica(remote);
                Applied++;
                Log.Info("Adopted " + vessel.GetDisplayName() + " " + oldId.ToString().Substring(0, 8) + " as " + remote.Label + ": the piece that came off here is their copy of it");
                return true;
            }
            return false;
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

        private bool SplitOffSomebodyElses(Vessel vessel, out RemoteVessel from)
        {
            from = null;
            if (vessel.parts == null || vessel.parts.Count == 0) return false;
            foreach (var remote in Registry.All)
            {
                if (remote.PartIds == null || !Registry.IsOwnedByOther(remote)) continue;
                for (var i = 0; i < vessel.parts.Count; i++)
                    if (vessel.parts[i] != null && remote.PartIds.Contains(vessel.parts[i].flightID)) { from = remote; return true; }
            }
            return false;
        }

        private string NameOf(int clientId) => Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;

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

        /// <summary>
        /// The Recover button, pressed. KSP recovers the vessel only once the space centre has loaded, and the
        /// scene change before that reported us gone from the flight: the server handed the rocket to the
        /// friend still aboard, then refused our removal because it was no longer ours, and the recovered
        /// rocket came back to us on the pad with a recovered Kerbal inside. Say it is gone now, first.
        /// </summary>
        private void OnRecoveryRequested(Vessel vessel)
        {
            if (vessel == null || !Registry.IsMine(vessel.id)) return;
            Log.Info("Recovery of " + vessel.GetDisplayName() + " requested; telling the server before the scene changes");
            _recoveringOurs.Add(vessel.id);
            SendRemove(vessel.id, "recovered");
        }

        /// <summary>Our own vessels between the Recover button and KSP's recovery of them at the space centre: not zombies to discard.</summary>
        private readonly HashSet<Guid> _recoveringOurs = new HashSet<Guid>();

        private void OnVesselRecovered(ProtoVessel proto, bool quick)
        {
            if (proto != null) _recoveringOurs.Remove(proto.vesselID);
            // Ours only: the flight-scene button already said so (the id is no longer known), and another
            // player's vessel is theirs to report.
            if (proto == null || !Registry.IsMine(proto.vesselID)) return;
            if (proto != null) SendRemove(proto.vesselID, "recovered");
        }

        private void OnVesselTerminated(ProtoVessel proto)
        {
            if (proto != null && Registry.IsMine(proto.vesselID)) SendRemove(proto.vesselID, "terminated");
        }

        private void OnSceneLoadRequested(GameScenes scene)
        {
            _sceneChanging = true;
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null) return;
            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel != null && Registry.IsMine(vessel.id) && vessel.id != _keepThroughRevert) SendProto(vessel, ProtoReason.LeavingFlight);
            }
            Addon.Authority.ReleaseAll("leaving flight", _keepThroughRevert);
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            _sceneChanging = false;
            _keepThroughRevert = Guid.Empty;
            DiscardRemovedZombies();
            ApplyPending();
        }

        /// <summary>
        /// A scene load brings the save's vessels back, and the save can hold copies of vessels the server has
        /// since removed: the craft a co-pilot rode home when its pilot recovered it stays in the flight state
        /// until the space centre loads. Those come back from the save with our Kerbal still "aboard". The
        /// new-vessel scan only sees flight-scene creations, so this runs on every game scene.
        /// </summary>
        private void DiscardRemovedZombies()
        {
            if (FlightGlobals.fetch == null || FlightGlobals.Vessels == null) return;
            var doomed = new List<Vessel>();
            foreach (var vessel in FlightGlobals.Vessels)
                if (vessel != null && vessel.id != Guid.Empty && !Registry.IsKnown(vessel.id) && Registry.WasRemoved(vessel.id) && !_recoveringOurs.Contains(vessel.id)) doomed.Add(vessel);
            for (var i = 0; i < doomed.Count; i++)
            {
                Log.Info("Discarding " + doomed[i].GetDisplayName() + ": it was removed earlier and only came back with the " + SceneName(HighLogic.LoadedScene) + " load");
                VesselLoader.Discard(doomed[i]);
            }
        }

        private static string SceneName(GameScenes scene) => scene.ToString().ToLowerInvariant().Replace("spacecenter", "space centre").Replace("trackstation", "tracking station");
    }
}
