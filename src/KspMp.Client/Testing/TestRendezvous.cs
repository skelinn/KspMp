using System;
using KspMp.Vessels;
using UnityEngine;

namespace KspMp.Testing
{
    /// <summary>
    /// Test-harness only. Flying an orbital rendezvous by hand cannot be scripted, so these helpers put a vessel
    /// where a test needs it: a circular orbit, then alongside another vessel, then port-to-port at magnet range.
    /// Only reachable through -kspmp- launch options; nothing here runs during normal play.
    /// </summary>
    public static class TestRendezvous
    {
        /// <summary>Places a vessel in a circular equatorial orbit at the given altitude above its current body.</summary>
        public static bool PlaceInCircularOrbit(Vessel vessel, double altitudeMetres)
        {
            if (vessel == null || vessel.mainBody == null) return false;
            var body = vessel.mainBody;
            var ut = Planetarium.GetUniversalTime();
            try
            {
                // Clear the landed state first: GoOnRails on a landed vessel pins it to the surface, and then
                // setting an orbit does nothing because KSP keeps recomputing the position from the ground.
                vessel.Landed = false;
                vessel.Splashed = false;
                vessel.landedAt = string.Empty;
                vessel.displaylandedAt = string.Empty;
                vessel.situation = Vessel.Situations.ORBITING;
                if (!vessel.packed) vessel.GoOnRails();
                vessel.orbitDriver.orbit.SetOrbit(0, 0, body.Radius + altitudeMetres, 0, 0, 0, ut, body);
                vessel.orbitDriver.UpdateOrbit();
                vessel.orbitDriver.updateFromParameters();
                OrbitPhysicsManager.CheckReferenceFrame();
                OrbitPhysicsManager.HoldVesselUnpack(10);
                vessel.IgnoreGForces(20);
                Log.Info("Test: placed " + vessel.GetDisplayName() + " in a " + (altitudeMetres / 1000).ToString("F0") + " km circular orbit of " + body.bodyName
                         + "; altitude now " + (vessel.altitude / 1000).ToString("F1") + " km, situation " + vessel.situation);
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("Test: placing " + vessel.GetDisplayName() + " in orbit", e);
                return false;
            }
        }

        /// <summary>
        /// Coarse step: copies the target's orbit and offsets us by a few hundred metres, so the target comes
        /// inside physics range and both vessels become real on this client.
        /// </summary>
        public static bool MoveNear(Vessel ours, Vessel target, float metres)
        {
            if (ours == null || target == null || target.orbit == null) return false;
            var ut = Planetarium.GetUniversalTime();
            var body = target.mainBody;
            try
            {
                var pos = target.orbit.getRelativePositionAtUT(ut);
                var vel = target.orbit.getOrbitalVelocityAtUT(ut);
                // Offset sideways (normal to the orbit plane) so we do not sit in the target's path.
                var sideways = Vector3d.Cross(pos, vel).normalized * metres;
                ours.Landed = false;
                ours.Splashed = false;
                ours.landedAt = string.Empty;
                ours.situation = Vessel.Situations.ORBITING;
                if (!ours.packed) ours.GoOnRails();
                ours.orbit.UpdateFromStateVectors(pos + sideways, vel, body, ut);
                ours.orbitDriver.UpdateOrbit();
                ours.orbitDriver.updateFromParameters();
                OrbitPhysicsManager.CheckReferenceFrame();
                OrbitPhysicsManager.HoldVesselUnpack(10);
                ours.IgnoreGForces(20);
                Log.Info("Test: moved " + ours.GetDisplayName() + " to " + metres.ToString("F0") + " m from " + target.GetDisplayName());
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("Test: moving next to " + target.GetDisplayName(), e);
                return false;
            }
        }

        public static ModuleDockingNode FindFreePort(Vessel vessel, bool explain = false)
        {
            if (vessel == null || vessel.parts == null)
            {
                if (explain) Log.Warn("Test:   " + (vessel == null ? "vessel is null" : vessel.GetDisplayName() + " has no parts list"));
                return null;
            }
            var seen = 0;
            for (var i = 0; i < vessel.parts.Count; i++)
            {
                var modules = vessel.parts[i].Modules;
                for (var m = 0; m < modules.Count; m++)
                {
                    if (!(modules[m] is ModuleDockingNode node)) continue;
                    seen++;
                    if (node.nodeTransform == null)
                    {
                        if (explain) Log.Warn("Test:   " + vessel.GetDisplayName() + " port on " + vessel.parts[i].partInfo.title + " has no nodeTransform yet");
                        continue;
                    }
                    if (node.state != null && node.state.StartsWith("Docked", StringComparison.Ordinal))
                    {
                        if (explain) Log.Warn("Test:   " + vessel.GetDisplayName() + " port state is " + node.state);
                        continue;
                    }
                    if (node.otherNode != null)
                    {
                        if (explain) Log.Warn("Test:   " + vessel.GetDisplayName() + " port is already paired");
                        continue;
                    }
                    return node;
                }
            }
            if (explain) Log.Warn("Test:   " + vessel.GetDisplayName() + ": " + seen + " docking module(s) across " + vessel.parts.Count + " part(s), none usable; loaded=" + vessel.loaded + " packed=" + vessel.packed);
            return null;
        }

        /// <summary>
        /// Fine step: rigidly moves our loaded vessel so its docking port sits just in front of the target's port,
        /// facing it, with matching velocity. KSP's magnets then pull the two together on their own.
        /// </summary>
        public static bool AlignPorts(Vessel ours, Vessel target, float gapMetres, float closingSpeed = 0f)
        {
            if (ours == null || target == null || !ours.loaded || !target.loaded) return false;
            var ourNode = FindFreePort(ours, explain: true);
            var theirNode = FindFreePort(target, explain: true);
            if (ourNode == null || theirNode == null)
            {
                Log.Warn("Test: no usable port; ours=" + (ourNode != null) + " theirs=" + (theirNode != null)
                         + " (our vessel loaded=" + ours.loaded + " packed=" + ours.packed + ", target loaded=" + target.loaded + " packed=" + target.packed + ")");
                return false;
            }
            try
            {
                if (ours.packed) ours.GoOffRails();
                var theirPort = theirNode.nodeTransform;
                var ourPort = ourNode.nodeTransform;

                // Where our port must end up: in front of theirs, pointing back at it.
                var wantedPos = theirPort.position + theirPort.forward * gapMetres;
                var wantedRot = Quaternion.LookRotation(-theirPort.forward, theirPort.up);

                // Move the whole vessel by the rigid transform that takes our port to that pose.
                var delta = wantedRot * Quaternion.Inverse(ourPort.rotation);
                ours.SetRotation(delta * ours.transform.rotation, true);
                var offset = wantedPos - ourPort.position;
                ours.SetPosition(ours.transform.position + offset);
                // A real dock ends with a slow drift onto the port. Matching velocity exactly leaves the two
                // ships hanging just apart, and KSP's magnets never trigger, so add a gentle closing speed.
                var approach = (theirPort.position - ourPort.position).normalized;
                ours.SetWorldVelocity(WorldVelocityOf(target) + (Vector3d)(approach * closingSpeed));
                ours.IgnoreGForces(20);

                var achieved = (ourPort.position - theirPort.position).magnitude;
                var facing = Vector3.Dot(ourPort.forward, -theirPort.forward);
                Log.Info("Test: aligned " + ours.GetDisplayName() + " port to " + target.GetDisplayName()
                         + "; gap " + achieved.ToString("F2") + " m (acquire range " + theirNode.acquireRange.ToString("F2") + " m)"
                         + ", facing " + facing.ToString("F2") + " (1.0 is head on)"
                         + ", states " + ourNode.state + " / " + theirNode.state
                         + ", immortal ours=" + VesselImmortal.IsImmortal(ours) + " theirs=" + VesselImmortal.IsImmortal(target)
                         + ", closing at " + closingSpeed.ToString("F2") + " m/s");
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("Test: aligning docking ports", e);
                return false;
            }
        }

        /// <summary>
        /// The velocity to copy so we drift alongside a vessel rather than into it. A landed vessel's orbital
        /// velocity still carries the planet's rotation (~175 m/s at the equator), so using it would fling us.
        /// </summary>
        private static Vector3d WorldVelocityOf(Vessel vessel)
        {
            if (vessel.loaded && vessel.rootPart != null && vessel.rootPart.rb != null) return vessel.rootPart.rb.velocity;
            if (vessel.LandedOrSplashed) return Vector3d.zero;
            return vessel.GetObtVelocity() - Krakensbane.GetFrameVelocity();
        }

        /// <summary>The nearest vessel with a free docking port that this client does not simulate.</summary>
        public static Vessel FindDockingTarget(Vessel ours, Func<Guid, bool> ownedByOther)
        {
            if (ours == null || FlightGlobals.fetch == null) return null;
            Log.Info("Test: looking for a docking target among " + FlightGlobals.Vessels.Count + " vessel(s); ours is " + ours.GetDisplayName() + " " + ours.id.ToString().Substring(0, 8));
            Vessel best = null;
            var bestDistance = double.MaxValue;
            var all = FlightGlobals.Vessels;
            for (var i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || candidate.id == ours.id) continue;
                if (!ownedByOther(candidate.id))
                {
                    Log.Info("Test:   skipping " + candidate.GetDisplayName() + " " + candidate.id.ToString().Substring(0, 8) + " (not another player's)");
                    continue;
                }
                if (candidate.loaded && FindFreePort(candidate) == null)
                {
                    Log.Info("Test:   skipping " + candidate.GetDisplayName() + " (loaded, no free docking port)");
                    continue;
                }
                var distance = (candidate.GetWorldPos3D() - ours.GetWorldPos3D()).magnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }
            return best;
        }
    }
}
