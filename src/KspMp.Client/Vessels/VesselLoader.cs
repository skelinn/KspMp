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
                    reloadingActive = true;
                    force = true;
                    SkipReported.Remove(proto.vesselID);
                }
                var existingParts = existing.loaded ? existing.parts.Count : existing.protoVessel != null ? existing.protoVessel.protoPartSnapshots.Count : -1;
                // Compare who is aboard, not how many: swapping one kerbal for another, which is exactly what
                // happens around an EVA, leaves the count identical and used to be read as "nothing changed".
                var existingCrew = CrewSignature(existing);
                if (!force && existingParts == proto.protoPartSnapshots.Count && existingCrew == CrewSignature(proto))
                    return Outcome.Unchanged;

                Log.Info("Reloading vessel " + label + " (" + existingParts + " -> " + proto.protoPartSnapshots.Count + " parts" + (reloadingActive ? ", active vessel" : "") + ")");
                if (reloadingActive && existing.loaded)
                {
                    foreach (var part in existing.parts)
                        foreach (var crew in part.protoModuleCrew.ToArray())
                            existing.RemoveCrew(crew);
                    existing.DespawnCrew();
                }
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
                return Outcome.Failed;
            }
            proto.vesselRef.protoVessel = proto;
            if (proto.vesselRef.situation > Vessel.Situations.PRELAUNCH) proto.vesselRef.orbitDriver.updateFromParameters();
            if (double.IsNaN(proto.vesselRef.orbitDriver.pos.x))
            {
                Log.Warn("Snapshot of " + label + " has an invalid orbit");
                return Outcome.Failed;
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

        public static void Remove(Guid vesselId, string why)
        {
            try
            {
                var flightState = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flightState : null;
                var vessel = FlightGlobals.fetch != null ? FlightGlobals.FindVessel(vesselId) : null;
                if (vessel != null)
                {
                    if (vessel.isActiveVessel)
                    {
                        Log.Warn("Server removed our active vessel (" + why + "); keeping it loaded");
                        return;
                    }
                    Log.Info("Removing vessel " + vessel.GetDisplayName() + " (" + why + ")");
                    if (vessel.loaded) vessel.Unload();
                    FlightGlobals.RemoveVessel(vessel);
                    UnityEngine.Object.Destroy(vessel.gameObject);
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
