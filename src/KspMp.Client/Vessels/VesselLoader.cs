using System;
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

        public static bool GameReady =>
            HighLogic.LoadedSceneIsGame && HighLogic.CurrentGame != null && HighLogic.CurrentGame.flightState != null
            && FlightGlobals.fetch != null && (!HighLogic.LoadedSceneIsFlight || FlightGlobals.ready);

        public static Outcome Load(ProtoVessel proto, bool force)
        {
            if (!GameReady) return Outcome.Deferred;
            try
            {
                IsLoadingRemote = true;
                return LoadIntoGame(proto, force);
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

        private static Outcome LoadIntoGame(ProtoVessel proto, bool force)
        {
            var label = proto.vesselName + " " + proto.vesselID.ToString().Substring(0, 8);
            var existing = FlightGlobals.FindVessel(proto.vesselID);
            var hadExisting = existing != null;
            if (existing != null)
            {
                if (existing.isActiveVessel)
                {
                    Log.Warn("Not reloading the active vessel " + label + " from a snapshot");
                    return Outcome.Skipped;
                }
                var existingParts = existing.loaded ? existing.parts.Count : existing.protoVessel != null ? existing.protoVessel.protoPartSnapshots.Count : -1;
                var existingCrew = existing.loaded ? existing.GetCrewCount() : existing.protoVessel != null ? existing.protoVessel.GetVesselCrew().Count : -1;
                if (!force && existingParts == proto.protoPartSnapshots.Count && existingCrew == proto.GetVesselCrew().Count)
                    return Outcome.Unchanged;

                Log.Info("Reloading vessel " + label + " (" + existingParts + " -> " + proto.protoPartSnapshots.Count + " parts)");
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
                Log.Info("Loading vessel " + label + " (" + proto.protoPartSnapshots.Count + " parts, " + proto.situation + ")");
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
                    Log.Info("Removing vessel " + vessel.vesselName + " (" + why + ")");
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
