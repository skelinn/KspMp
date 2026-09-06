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

        public static bool IsImmortal(Vessel vessel) => vessel != null && vessel.rootPart != null && float.IsPositiveInfinity(vessel.rootPart.crashTolerance);

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
            if (vessel.rootPart != null && float.IsPositiveInfinity(vessel.rootPart.crashTolerance) == immortal) return;

            var buoyancy = vessel.GetComponent<PartBuoyancy>();
            if (buoyancy) buoyancy.enabled = !immortal;
            var collisionEnhancer = vessel.GetComponent<CollisionEnhancer>();
            if (collisionEnhancer) collisionEnhancer.enabled = !immortal;
            var integrator = vessel.GetComponent<FlightIntegrator>();
            if (integrator) integrator.enabled = !immortal;
            SetKerbalFrozen(vessel, immortal);

            if (!vessel.loaded || vessel.parts == null) return;
            Log.Info("Vessel " + vessel.GetDisplayName() + " is now " + (immortal ? "immortal (replica)" : "mortal (ours)"));
            foreach (var part in vessel.parts)
            {
                if (part == null) continue;
                if (immortal)
                {
                    if (!SavedValues.ContainsKey(part))
                        SavedValues[part] = new Saved { CrashTolerance = part.crashTolerance, MaxPressure = part.maxPressure };
                    part.crashTolerance = float.PositiveInfinity;
                    part.maxPressure = double.PositiveInfinity;
                }
                else if (SavedValues.TryGetValue(part, out var saved))
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
    }
}
