using System.Collections.Generic;

namespace KspMp.Vessels
{
    /// <summary>
    /// A replica must never explode, overheat or be pushed by our physics, because its owner's simulation is the truth.
    /// Same approach as LunaMultiplayer's SetImmortal: disable the integrator, buoyancy and collision enhancer and make
    /// every part indestructible; restored when we take ownership.
    /// </summary>
    public static class VesselImmortal
    {
        private struct Saved
        {
            public float CrashTolerance;
            public double MaxPressure;
        }

        private static readonly Dictionary<Part, Saved> SavedValues = new Dictionary<Part, Saved>();
        /// <summary>
        /// Which vessels are replicas right now, by id. This used to be read back off the root part's crash
        /// tolerance, and for a packed piece of debris that probe never agreed with what had been set: the replica
        /// re-immortalised it every physics step and said so in the log - 5,666 times in one 85-second flight.
        /// </summary>
        private static readonly HashSet<System.Guid> Immortal = new HashSet<System.Guid>();

        public static bool IsImmortal(Vessel vessel) => vessel != null && Immortal.Contains(vessel.id);

        /// <summary>The registry was wiped (scene change, disconnect): nothing is a replica any more.</summary>
        public static void Reset()
        {
            Immortal.Clear();
            SavedValues.Clear();
        }

        /// <summary>
        /// A kerbal is not a rocket: KerbalEVA runs its own state machine every frame, so a replica kerbal
        /// walks, flails and ragdolls against the pose its owner is sending. Freezing the controller and its
        /// rigidbodies leaves the pose to us. Set <c>-kspmp-evamode live</c> to leave the controller running -
        /// remote kerbals never take local input anyway, since that requires being the active vessel.
        /// </summary>
        public static bool FreezeRemoteKerbals = true;

        private static void SetKerbalFrozen(Vessel vessel, bool frozen)
        {
            if (!FreezeRemoteKerbals || vessel == null || !vessel.isEVA || !vessel.loaded) return;
            var eva = vessel.evaController;
            if (eva != null && eva.enabled == frozen)
            {
                eva.enabled = !frozen;
                Log.Info("Kerbal " + vessel.GetDisplayName() + " is now " + (frozen ? "posed by its owner" : "under its own control"));
            }
            if (vessel.parts == null) return;
            foreach (var part in vessel.parts)
            {
                if (part == null || part.rb == null) continue;
                part.rb.isKinematic = frozen;
            }
        }

        public static void Set(Vessel vessel, bool immortal)
        {
            if (vessel == null) return;
            if (Immortal.Contains(vessel.id) == immortal)
            {
                // Already so. KSP can still hand a part fresh numbers (a packed vessel unpacking, a part
                // re-initialised), so keep them topped up - quietly, this runs every physics step.
                if (immortal && vessel.loaded && vessel.parts != null) Harden(vessel);
                return;
            }
            if (immortal) Immortal.Add(vessel.id); else Immortal.Remove(vessel.id);

            var buoyancy = vessel.GetComponent<PartBuoyancy>();
            if (buoyancy) buoyancy.enabled = !immortal;
            var collisionEnhancer = vessel.GetComponent<CollisionEnhancer>();
            if (collisionEnhancer) collisionEnhancer.enabled = !immortal;
            var integrator = vessel.GetComponent<FlightIntegrator>();
            if (integrator) integrator.enabled = !immortal;
            SetKerbalFrozen(vessel, immortal);

            if (!vessel.loaded || vessel.parts == null) return;
            Log.Info("Vessel " + vessel.GetDisplayName() + " is now " + (immortal ? "immortal (replica)" : "mortal (ours)"));
            if (immortal) { Harden(vessel); return; }
            foreach (var part in vessel.parts)
            {
                if (part == null) continue;
                if (SavedValues.TryGetValue(part, out var saved))
                {
                    part.crashTolerance = saved.CrashTolerance;
                    part.maxPressure = saved.MaxPressure;
                    SavedValues.Remove(part);
                }
                else
                {
                    part.crashTolerance = part.partInfo != null && part.partInfo.partPrefab != null ? part.partInfo.partPrefab.crashTolerance : 9f;
                    part.maxPressure = part.partInfo != null && part.partInfo.partPrefab != null ? part.partInfo.partPrefab.maxPressure : 4000.0;
                }
            }
        }

        private static void Harden(Vessel vessel)
        {
            foreach (var part in vessel.parts)
            {
                if (part == null || float.IsPositiveInfinity(part.crashTolerance)) continue;
                if (!SavedValues.ContainsKey(part))
                    SavedValues[part] = new Saved { CrashTolerance = part.crashTolerance, MaxPressure = part.maxPressure };
                part.crashTolerance = float.PositiveInfinity;
                part.maxPressure = double.PositiveInfinity;
            }
        }
    }
}
