using System;
using System.Collections.Generic;
using KSP.UI.Screens;

namespace KspMp.Vessels
{
    /// <summary>Puts server snapshots into the running game and takes vessels out again (LunaMultiplayer's VesselLoader sequence, MIT).</summary>
    public static class VesselLoader
    {
        public enum Outcome
        {
            Loaded,
            Reloaded,
            Unchanged,
            Skipped,
            Failed,
            Deferred,
            /// <summary>The snapshot's orbit produced no position: taken the frame the vessel was born. Worth a retry with a state.</summary>
            InvalidOrbit,
        }

        /// <summary>True while a remote snapshot is being instantiated, so vessel-create events are not mistaken for local launches.</summary>
        public static bool IsLoadingRemote { get; private set; }

        /// <summary>Vessels we already reported as "skipped because active", so periodic snapshots stay quiet.</summary>
        private static readonly HashSet<Guid> SkipReported = new HashSet<Guid>();

        public static bool GameReady =>
            HighLogic.LoadedSceneIsGame && HighLogic.CurrentGame != null && HighLogic.CurrentGame.flightState != null
            && FlightGlobals.fetch != null && (!HighLogic.LoadedSceneIsFlight || FlightGlobals.ready);

        public static Outcome Load(ProtoVessel proto, bool force) => Load(proto, force, false);

        public static Outcome Load(ProtoVessel proto, bool force, bool allowActiveReload)
        {
            if (!GameReady) return Outcome.Deferred;
            try
            {
                IsLoadingRemote = true;
                return LoadIntoGame(proto, force, allowActiveReload);
            }
            catch (Exception e)
            {
                Log.Exception("Loading vessel " + proto.vesselID, e);
                return Outcome.Failed;
            }
            finally
            {
                IsLoadingRemote = false;
            }
        }

        /// <summary>Who is aboard, in order, as one comparable string.</summary>
        private static string CrewSignature(Vessel vessel)
        {
            if (vessel == null) return "";
            var crew = vessel.loaded ? vessel.GetVesselCrew() : vessel.protoVessel != null ? vessel.protoVessel.GetVesselCrew() : null;
            return CrewSignature(crew);
        }

        private static string CrewSignature(ProtoVessel proto) => proto == null ? "" : CrewSignature(proto.GetVesselCrew());

        /// <summary>Whether a snapshot puts an unloaded vessel somewhere else than its current copy.</summary>
        private static bool Moved(Vessel existing, ProtoVessel proto)
        {
            if (existing.situation != proto.situation) return true;
            if ((existing.landedAt ?? "") != (proto.landedAt ?? "")) return true;
            // Not the orbit itself: an unloaded copy's orbit follows the owner's streamed states, and a snapshot
            // is up to thirty seconds old, so an ascending rocket would differ every time and be rebuilt every
            // periodic snapshot. Situation, site and body are the discrete facts states do not carry.
            var orbit = existing.orbit;
            var snap = proto.orbitSnapShot;
            if (orbit == null || snap == null) return false;
            return orbit.referenceBody != null && orbit.referenceBody.flightGlobalsIndex != snap.ReferenceBodyIndex;
        }

        private static string CrewSignature(System.Collections.Generic.List<ProtoCrewMember> crew)
        {
            if (crew == null || crew.Count == 0) return "";
            var names = new string[crew.Count];
            for (var i = 0; i < crew.Count; i++) names[i] = crew[i] != null ? crew[i].name : "?";
            return string.Join("|", names);
        }

        /// <summary>
        /// A kerbal on EVA is a vessel whose one part must have a crew member: ProtoPartSnapshot.Load reads
        /// protoModuleCrew[0] without checking, deep inside Vessel.Load() where our try/catch cannot reach it.
        /// If the roster has not caught up with the kerbal yet, defer the snapshot rather than load it.
        /// </summary>
        /// <summary>Whether other players' kerbals on EVA are loaded at all (Settings.EvaSync).</summary>
        public static bool LoadRemoteEva = true;

        private static bool EvaCrewIsMissing(ProtoVessel proto, out string why)
        {
            why = null;
            if (proto == null || proto.vesselType != VesselType.EVA) return false;
            var crew = proto.GetVesselCrew();
            if (crew == null || crew.Count == 0 || crew[0] == null || string.IsNullOrEmpty(crew[0].name))
            {
                why = "it carries no crew at all";
                return true;
            }
            var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster != null && !roster.Exists(crew[0].name))
            {
                why = crew[0].name + " is not in our roster yet";
                return true;
            }
            return false;
        }

        private static Outcome LoadIntoGame(ProtoVessel proto, bool force, bool allowActiveReload)
        {
            var label = KSP.Localization.Localizer.Format(proto.vesselName) + " " + proto.vesselID.ToString().Substring(0, 8);
            if (!LoadRemoteEva && proto.vesselType == VesselType.EVA)
            {
                if (SkipReported.Add(proto.vesselID)) Log.Info("Not loading the EVA kerbal " + label + ": EVA sync is off");
                return Outcome.Skipped;
            }
            if (EvaCrewIsMissing(proto, out var why))
            {
                Log.Info("Holding the EVA snapshot of " + label + ": " + why);
                return Outcome.Deferred;
            }
            var existing = FlightGlobals.FindVessel(proto.vesselID);
            var hadExisting = existing != null;
            var reloadingActive = false;
            ProtoVessel previous = null;
            if (existing != null)
            {
                if (existing.isActiveVessel)
                {
                    if (!allowActiveReload)
                    {
                        if (SkipReported.Add(proto.vesselID))
                            Log.Info("Keeping the active vessel " + label + " as it is; snapshots of it are ignored while we fly it");
                        return Outcome.Skipped;
                    }
                    // Sitting in a vessel somebody else flies: its parts are theirs to change, and a snapshot with a
                    // different set of them - a stage they fired, a fairing they dropped, an escape tower they
                    // jettisoned - is how our copy finds out. The same parts and crew are not worth tearing our own
                    // seat out from under us for, which is what a reload does.
                    if (!force && SamePartIds(existing, proto) && CrewSignature(existing) == CrewSignature(proto))
                        return Outcome.Unchanged;
                    reloadingActive = true;
                    force = true;
                    SkipReported.Remove(proto.vesselID);
                    Log.Info("Refreshing " + label + ", the vessel we are aboard: its parts or crew changed");
                }
                // A copy outside physics range is not moved by states; only a snapshot replaces it. One with the
                // same parts used to count as unchanged, so a rocket that left the pad without staging still sat
                // on the pad for a player at the space centre - and blocked their launch.
                if (!force && !existing.loaded && Moved(existing, proto))
                {
                    Log.Info("Moving vessel " + label + ": " + existing.situation + (string.IsNullOrEmpty(existing.landedAt) ? "" : " at " + existing.landedAt) + " -> " + proto.situation + (string.IsNullOrEmpty(proto.landedAt) ? "" : " at " + proto.landedAt));
                    force = true;
                }
                var existingParts = existing.loaded ? existing.parts.Count : existing.protoVessel != null ? existing.protoVessel.protoPartSnapshots.Count : -1;
                // Compare who is aboard, not how many: swapping one kerbal for another, which is exactly what
                // happens around an EVA, leaves the count identical and used to be read as "nothing changed".
                var existingCrew = CrewSignature(existing);
                if (!force && existingParts == proto.protoPartSnapshots.Count && existingCrew == CrewSignature(proto))
                    return Outcome.Unchanged;

                Log.Info("Reloading vessel " + label + " (" + existingParts + " -> " + proto.protoPartSnapshots.Count + " parts" + (reloadingActive ? ", active vessel" : "") + ")");
                if (existing.loaded)
                {
                    // Any loaded copy: a kerbal left seated in a destroyed part keeps a KerbalRef to it and its
                    // IVA leaks; the snapshot seats them again in the new one.
                    foreach (var part in existing.parts)
                        foreach (var crew in part.protoModuleCrew.ToArray())
                            existing.RemoveCrew(crew);
                    existing.DespawnCrew();
                }
                // A copy of what we are replacing, to put back if the snapshot turns out not to load.
                try { previous = existing.BackupVessel(); } catch (Exception e) { Log.Exception("Backing up " + label + " before its reload", e); }
                VesselImmortal.Forget(existing);
                FlightGlobals.RemoveVessel(existing);
                HighLogic.CurrentGame.flightState.protoVessels.RemoveAll(p => p == null || p.vesselID == existing.id);
                existing.gameObject.SetActive(false);
                if (existing.parts != null)
                    foreach (var part in existing.parts)
                        UnityEngine.Object.Destroy(part.gameObject);
                UnityEngine.Object.Destroy(existing.gameObject);
            }
            else
            {
                var crew = proto.GetVesselCrew();
                var crewNames = crew != null && crew.Count > 0 ? string.Join(", ", crew.ConvertAll(c => c != null ? c.name : "?").ToArray()) : "no crew";
                Log.Info("Loading vessel " + label + " (" + proto.protoPartSnapshots.Count + " parts, " + proto.situation + ", " + crewNames + ")");
            }

            proto.Load(HighLogic.CurrentGame.flightState);
            if (proto.vesselRef == null)
            {
                Log.Warn("Snapshot of " + label + " did not create a vessel");
                Restore(previous, label, reloadingActive);
                return Outcome.Failed;
            }
            proto.vesselRef.protoVessel = proto;
            if (proto.vesselRef.situation > Vessel.Situations.PRELAUNCH) proto.vesselRef.orbitDriver.updateFromParameters();
            if (double.IsNaN(proto.vesselRef.orbitDriver.pos.x))
            {
                Log.Warn("Snapshot of " + label + " has an invalid orbit; waiting for a state to take one from");
                Discard(proto.vesselRef);
                Restore(previous, label, reloadingActive);
                return Outcome.InvalidOrbit;
            }
            if (reloadingActive)
            {
                proto.vesselRef.Load();
                proto.vesselRef.RebuildCrewList();
                FlightGlobals.ForceSetActiveVessel(proto.vesselRef);
                proto.vesselRef.SpawnCrew();
            }
            RefreshMarkers();
            return hadExisting ? Outcome.Reloaded : Outcome.Loaded;
        }

        private static bool SamePartIds(Vessel existing, ProtoVessel proto)
        {
            var ids = new HashSet<uint>();
            if (existing.loaded && existing.parts != null) foreach (var part in existing.parts) ids.Add(part.flightID);
            else if (existing.protoVessel != null) foreach (var part in existing.protoVessel.protoPartSnapshots) ids.Add(part.flightID);
            else return false;
            if (ids.Count != proto.protoPartSnapshots.Count) return false;
            foreach (var part in proto.protoPartSnapshots) if (!ids.Contains(part.flightID)) return false;
            return true;
        }

        /// <summary>Takes a vessel out of the game without telling anyone. The caller has said why.</summary>
        /// <summary>The snapshot could not be loaded: put the copy it was replacing back, rather than have nothing.</summary>
        private static void Restore(ProtoVessel previous, string label, bool wasActive)
        {
            if (previous == null) return;
            try
            {
                previous.Load(HighLogic.CurrentGame.flightState);
                if (previous.vesselRef == null) { Log.Warn("Could not put the previous copy of " + label + " back either"); return; }
                previous.vesselRef.protoVessel = previous;
                if (wasActive)
                {
                    previous.vesselRef.Load();
                    previous.vesselRef.RebuildCrewList();
                    FlightGlobals.ForceSetActiveVessel(previous.vesselRef);
                    previous.vesselRef.SpawnCrew();
                }
                Log.Info("Put the previous copy of " + label + " back");
            }
            catch (Exception e)
            {
                Log.Exception("Putting the previous copy of " + label + " back", e);
            }
        }

        public static void Discard(Vessel vessel)
        {
            if (vessel == null) return;
            try
            {
                VesselImmortal.Forget(vessel);
                var id = vessel.id;
                if (vessel.loaded) vessel.Unload();
                FlightGlobals.RemoveVessel(vessel);
                HighLogic.CurrentGame?.flightState?.protoVessels.RemoveAll(p => p == null || p.vesselID == id);
                if (vessel.parts != null)
                    foreach (var part in vessel.parts)
                        if (part != null) UnityEngine.Object.Destroy(part.gameObject);
                UnityEngine.Object.Destroy(vessel.gameObject);
                RefreshMarkers();
            }
            catch (Exception e)
            {
                Log.Exception("Discarding vessel " + vessel.id, e);
            }
        }

        public static void Remove(Guid vesselId, string why)
        {
            try
            {
                var flightState = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flightState : null;
                var vessel = FlightGlobals.fetch != null ? FlightGlobals.FindVessel(vesselId) : null;
                if (vessel != null)
                {
                    var roster = KspMpAddon.Instance != null ? KspMpAddon.Instance.Roster : null;
                    var avatarAboard = roster != null && roster.AvatarAboard(vessel);
                    if (vessel.isActiveVessel)
                    {
                        // The vessel we are sitting in no longer exists for anybody else: its pilot crashed it,
                        // recovered it, or reverted it away. Keeping a copy alive here left the player flying a
                        // ghost, with their Kerbal assigned to it for good. It dies here the way it did there,
                        // and KSP does what it does when the active vessel is lost.
                        Log.Warn("Server removed the vessel we are aboard, " + vessel.GetDisplayName() + " (" + why + "); it is gone here too");
                        // Vessel.Die() leaves the active vessel's parts alone (Vessel.cs:8696); blowing the parts
                        // up is how KSP itself loses an active vessel, and what follows is stock behaviour.
                        VesselImmortal.Set(vessel, false);
                        // The deaths this causes are the owner's to report, and they already have.
                        if (roster != null) roster.QuietCrewOf(vessel, 10f);
                        var parts = vessel.parts != null ? vessel.parts.ToArray() : new Part[0];
                        for (var i = 0; i < parts.Length; i++)
                            if (parts[i] != null) parts[i].explode();
                        if (avatarAboard) roster.ReturnAvatar(why);
                        flightState?.protoVessels.RemoveAll(p => p == null || p.vesselID == vesselId);
                        return;
                    }
                    Log.Info("Removing vessel " + vessel.GetDisplayName() + " (" + why + ")");
                    if (vessel.loaded) vessel.Unload();
                    FlightGlobals.RemoveVessel(vessel);
                    UnityEngine.Object.Destroy(vessel.gameObject);
                    // After the vessel is out of FlightGlobals, so the roster reports the change (crew aboard a
                    // vessel somebody else simulates is otherwise theirs to report, and they never report our avatar).
                    if (avatarAboard) roster.ReturnAvatar(why);
                }
                flightState?.protoVessels.RemoveAll(p => p == null || p.vesselID == vesselId);
                RefreshMarkers();
            }
            catch (Exception e)
            {
                Log.Exception("Removing vessel " + vesselId, e);
            }
        }

        private static void RefreshMarkers()
        {
            if (KSCVesselMarkers.fetch != null) KSCVesselMarkers.fetch.RefreshMarkers();
        }
    }
}
