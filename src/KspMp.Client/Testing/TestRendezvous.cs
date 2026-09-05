using System;
using System.Reflection;
using System.Text;
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

                // KSP's magnets only reach inside the port's acquireRange (0.5 m on a Clamp-O-Tron),
                // so parking at the requested gap can leave the ports aligned but permanently outside
                // capture. Never sit further out than the range the port can actually act over.
                var gap = Mathf.Min(gapMetres, theirNode.acquireRange * 0.6f);

                // Where our port must end up: in front of theirs, pointing back at it.
                var wantedPos = theirPort.position + theirPort.forward * gap;
                var wantedRot = Quaternion.LookRotation(-theirPort.forward, theirPort.up);

                // Move the whole vessel by the rigid transform that takes our port to that pose.
                var delta = wantedRot * Quaternion.Inverse(ourPort.rotation);
                ours.SetRotation(delta * ours.transform.rotation, true);
                var offset = wantedPos - ourPort.position;
                ours.SetPosition(ours.transform.position + offset);
                // A real dock ends with a slow drift onto the port. Matching velocity exactly leaves the two
                // ships hanging just apart, and KSP's magnets never trigger, so add a gentle closing speed.
                var approach = (theirPort.position - ourPort.position).normalized;
                ApplyVelocity(ours, WorldVelocityOf(target) + (Vector3d)(approach * closingSpeed));

                var achieved = (ourPort.position - theirPort.position).magnitude;
                var relativeSpeed = (WorldVelocityOf(ours) - WorldVelocityOf(target)).magnitude;
                var facing = Vector3.Dot(ourPort.forward, -theirPort.forward);
                Log.Info("Test: aligned " + ours.GetDisplayName() + " port to " + target.GetDisplayName()
                         + "; gap " + achieved.ToString("F2") + " m (asked " + gapMetres.ToString("F2") + ", capped to " + gap.ToString("F2")
                         + ", acquire range " + theirNode.acquireRange.ToString("F2") + " m)"
                         + ", facing " + facing.ToString("F2") + " (1.0 is head on)"
                         + ", states " + ourNode.state + " / " + theirNode.state
                         + ", immortal ours=" + VesselImmortal.IsImmortal(ours) + " theirs=" + VesselImmortal.IsImmortal(target)
                         + ", closing at " + closingSpeed.ToString("F2") + " m/s"
                         + ", relative speed now " + relativeSpeed.ToString("F3") + " m/s");
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
        /// <summary>
        /// Renews the closing velocity without moving either ship. KSP re-derives part velocities every
        /// frame, so the single push AlignPorts gives fades almost at once and the ports hang just apart;
        /// call this every few frames while the approach finishes. Returns false once there is no free
        /// port left to aim at, which is what a completed capture looks like from here.
        /// </summary>
        public static bool HoldApproach(Vessel ours, Vessel target, float closingSpeed)
        {
            if (ours == null || target == null || !ours.loaded || !target.loaded || ours.packed) return false;
            var ourNode = FindFreePort(ours);
            var theirNode = FindFreePort(target);
            if (ourNode == null || theirNode == null) return false;
            var approach = theirNode.nodeTransform.position - ourNode.nodeTransform.position;
            ApplyVelocity(ours, WorldVelocityOf(target) + (Vector3d)(approach.normalized * closingSpeed));
            return true;
        }

        /// <summary>Port-to-port gap, facing and node states, for the docking test's progress log.</summary>
        public static string DescribePorts(Vessel ours, Vessel target)
        {
            var ourNode = FindFreePort(ours);
            var theirNode = FindFreePort(target);
            if (ourNode == null || theirNode == null)
                return "free port ours=" + (ourNode != null) + " theirs=" + (theirNode != null);
            var gap = (ourNode.nodeTransform.position - theirNode.nodeTransform.position).magnitude;
            var facing = Vector3.Dot(ourNode.nodeTransform.forward, -theirNode.nodeTransform.forward);
            var closing = Vector3d.Dot(WorldVelocityOf(target) - WorldVelocityOf(ours),
                                       (theirNode.nodeTransform.position - ourNode.nodeTransform.position).normalized);
            return "port gap " + gap.ToString("F2") + " m, facing " + facing.ToString("F2")
                   + ", closing " + (-closing).ToString("F3") + " m/s, states " + ourNode.state + " / " + theirNode.state;
        }

        /// <summary>
        /// Sets a vessel's velocity down to the part rigidbodies. Vessel.SetWorldVelocity alone does not
        /// reach them, so PhysX keeps whatever momentum an unpacked ship already had. Spin is zeroed so a
        /// lined-up pair of ports stays lined up.
        /// </summary>
        private static void ApplyVelocity(Vessel vessel, Vector3d velocity)
        {
            vessel.SetWorldVelocity(velocity);
            if (vessel.loaded && !vessel.packed)
            {
                var parts = vessel.parts;
                for (var i = 0; i < parts.Count; i++)
                {
                    var rb = parts[i].rb;
                    if (rb == null) continue;
                    rb.velocity = (Vector3)velocity;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            vessel.IgnoreGForces(20);
        }

        // Docking-node instrumentation. Read by reflection so this compiles whatever the exact KSP API
        // shape is, and so a renamed or missing member degrades to "not present" instead of a build break.
        private const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly string[] NodeFields =
        {
            "nodeType", "gendered", "genderFemale", "acquireRange", "acquireMinFwdDot", "acquireMinRollDot",
            "captureRange", "captureMinFwdDot", "captureMinRollDot", "minDistanceToReEngage",
            "snapRotation", "snapOffset", "state", "otherNode", "dockedPartUId", "deployAnimationController",
        };

        private static object Member(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var field = type.GetField(name, AnyMember);
            if (field != null) return field.GetValue(target);
            var property = type.GetProperty(name, AnyMember);
            return property != null && property.CanRead ? property.GetValue(target, null) : null;
        }

        private static bool HasMember(object target, string name)
        {
            if (target == null) return false;
            var type = target.GetType();
            return type.GetField(name, AnyMember) != null || type.GetProperty(name, AnyMember) != null;
        }

        /// <summary>
        /// Everything KSP's own docking logic decides on, for one node: the tunables it compares against,
        /// the finite-state-machine state, and - the point of the exercise - whether the node's own approach
        /// scan can see the port opposite it.
        /// </summary>
        public static string DescribeNode(ModuleDockingNode node, string label)
        {
            if (node == null) return label + "=(no free node)";
            var text = new StringBuilder(label).Append("{");
            for (var i = 0; i < NodeFields.Length; i++)
            {
                var name = NodeFields[i];
                if (!HasMember(node, name)) continue;
                var value = Member(node, name);
                if (value is ModuleDockingNode other) value = other.part != null ? other.part.name : "node";
                text.Append(name).Append('=').Append(value == null ? "null" : value.ToString()).Append(' ');
            }

            var fsm = Member(node, "fsm");
            var fsmState = Member(fsm, "currentStateName") ?? Member(Member(fsm, "currentState"), "name");
            text.Append("fsm=").Append(fsmState == null ? "none" : fsmState.ToString());

            // The decisive question: does the node itself find an approach? If this is null while the ports
            // are centimetres apart and lined up, detection is what is broken, not the approach we flew.
            var scan = node.GetType().GetMethod("FindNodeApproaches", AnyMember, null, Type.EmptyTypes, null);
            if (scan != null)
            {
                try
                {
                    var found = scan.Invoke(node, null) as ModuleDockingNode;
                    text.Append(" approach=").Append(found == null ? "NONE"
                        : (found.part != null && found.part.vessel != null ? found.part.vessel.GetDisplayName() : "found"));
                }
                catch (Exception e) { text.Append(" approach=threw ").Append(e.GetBaseException().GetType().Name); }
            }
            else text.Append(" approach=(no FindNodeApproaches)");

            // Whether the port is physically able to touch anything. Include inactive children: a trigger
            // parked on a deactivated GameObject is invisible to the default search, and "missing" and
            // "switched off" call for completely different fixes.
            var part = node.part;
            if (part != null)
            {
                text.Append(" partState=").Append(part.State);
                text.Append(" shielded=").Append(part.ShieldedFromAirstream);
                var colliders = part.GetComponentsInChildren<Collider>(true);
                text.Append(" colliders[");
                for (var i = 0; colliders != null && i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (i > 0) text.Append(", ");
                    text.Append(c.name)
                        .Append(c.enabled ? ":on" : ":off")
                        .Append(c.isTrigger ? ":trigger" : ":solid")
                        .Append(c.gameObject.activeInHierarchy ? ":active" : ":INACTIVE");
                }
                text.Append("]");
            }
            return text.Append("}").ToString();
        }

        /// <summary>
        /// Drives the dock through KSP's own entry point. The stock magnets never pair on a teleported
        /// approach - the node's approach scan returns nothing even with the ports centimetres apart, lined
        /// up and inside captureRange - so the test would otherwise never reach the code that matters.
        /// DockToVessel is what the mod patches, so everything under test (the couple gate, authority merge,
        /// id remap, proto resync) runs exactly as it would for a hand-flown dock.
        /// </summary>
        public static bool ForceDock(Vessel ours, Vessel target)
        {
            var ourNode = FindFreePort(ours);
            var theirNode = FindFreePort(target);
            if (ourNode == null || theirNode == null) return false;
            var gap = (ourNode.nodeTransform.position - theirNode.nodeTransform.position).magnitude;
            if (gap > ourNode.captureRange)
            {
                Log.Info("Test: not forcing the dock yet, ports are " + gap.ToString("F2") + " m apart (capture range "
                         + ourNode.captureRange.ToString("F2") + " m)");
                return false;
            }
            try
            {
                Log.Info("Test: ports are " + gap.ToString("F3") + " m apart and stock acquisition has not fired; "
                         + "docking through ModuleDockingNode.DockToVessel");
                ourNode.otherNode = theirNode;
                theirNode.otherNode = ourNode;
                ourNode.DockToVessel(theirNode);
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("Test: forcing the dock", e);
                ourNode.otherNode = null;
                theirNode.otherNode = null;
                return false;
            }
        }

        /// <summary>Full node instrumentation for both sides of an attempted dock.</summary>
        public static string DescribeDockingNodes(Vessel ours, Vessel target)
        {
            // KSP's node scan walks the loaded-vessel list, so a vessel missing from it is invisible to
            // docking no matter how close the ports are.
            var loaded = FlightGlobals.VesselsLoaded;
            var oursListed = loaded != null && loaded.Contains(ours);
            var theirsListed = loaded != null && loaded.Contains(target);
            return "VesselsLoaded=" + (loaded == null ? 0 : loaded.Count)
                   + " containsOurs=" + oursListed + " containsTheirs=" + theirsListed + " | "
                   + DescribeNode(FindFreePort(ours), "ours") + " | " + DescribeNode(FindFreePort(target), "theirs");
        }

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
