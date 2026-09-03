using System;
using KspMp.Shared.Protocol;
using UnityEngine;

namespace KspMp.Vessels
{
    /// <summary>
    /// Drives a vessel that another client simulates: interpolates between the last two received states and poses
    /// every part each physics step. Positioning follows LunaMultiplayer's VesselPositioner (MIT): orbit parameters
    /// are updated from the interpolated state vectors, rotation and lat/lon/alt are lerped, and rigidbodies are
    /// posed directly so PhysX does not fight us.
    /// </summary>
    public sealed class Replica
    {
        private readonly Orbit _fromOrbit = new Orbit();
        private readonly Orbit _toOrbit = new Orbit();
        private Vessel _vessel;
        private VesselStateMsg _from;
        private VesselStateMsg _to;
        private bool _hasTo;
        private bool _orbitsReady;
        private float _frame;
        private int _numFrames = 1;

        public Replica(Guid vesselId)
        {
            VesselId = vesselId;
        }

        public Guid VesselId { get; }
        public bool HasPendingUpdates => _hasTo;
        public double LastUt => _hasTo ? _to.Ut : 0;
        public int Applied { get; private set; }

        public Vessel Vessel
        {
            get
            {
                if (_vessel == null || _vessel.id != VesselId) _vessel = FlightGlobals.fetch != null ? FlightGlobals.FindVessel(VesselId) : null;
                return _vessel;
            }
        }

        public void Push(VesselStateMsg state)
        {
            if (_hasTo && state.Ut <= _to.Ut) return;
            _from = _hasTo ? _to : state;
            _to = state;
            _hasTo = true;
            _frame = 0;
            var duration = _to.Ut - _from.Ut;
            if (duration <= 0 || duration > 2) duration = Time.fixedDeltaTime;
            _numFrames = Math.Max(1, (int)Math.Round(duration / Time.fixedDeltaTime));
            _orbitsReady = false;
        }

        /// <summary>Called once per physics step (TimingManager BetterLateThanNever) with the current universal time.</summary>
        public void Apply(double ut)
        {
            if (!_hasTo) return;
            var vessel = Vessel;
            if (vessel == null) return;
            var body = GetBody(_to.BodyIndex);
            if (body == null) return;

            if (vessel.loaded && !VesselImmortal.IsImmortal(vessel)) VesselImmortal.Set(vessel, true);
            if (!_orbitsReady)
            {
                SetOrbit(_fromOrbit, ref _from);
                SetOrbit(_toOrbit, ref _to);
                _orbitsReady = true;
            }

            var pct = Mathf.Clamp01(_frame / _numFrames);
            try
            {
                ApplyState(vessel, body, ut, pct);
                Applied++;
            }
            finally
            {
                _frame++;
            }
        }

        public void Detach()
        {
            var vessel = Vessel;
            if (vessel != null) VesselImmortal.Set(vessel, false);
            _hasTo = false;
        }

        private void ApplyState(Vessel vessel, CelestialBody body, double ut, float pct)
        {
            // Orbit parameters from the interpolated state vectors. Part.ResumeVelocity derives rigidbody velocity from them.
            var posA = _fromOrbit.getRelativePositionAtUT(ut);
            var posB = _toOrbit.getRelativePositionAtUT(ut);
            var velA = _fromOrbit.getOrbitalVelocityAtUT(ut);
            var velB = _toOrbit.getOrbitalVelocityAtUT(ut);
            vessel.orbit.UpdateFromStateVectors(Vector3d.Lerp(posA, posB, pct), Vector3d.Lerp(velA, velB, pct), body, ut);

            vessel.staticPressurekPa = FlightGlobals.getStaticPressure(_to.Altitude, body);
            vessel.heightFromTerrain = _to.HeightFromTerrain;

            var rotation = Quaternion.Slerp(Rotation(ref _from), Rotation(ref _to), pct);
            vessel.srfRelRotation = rotation;
            var useTarget = pct >= 0.5f;
            vessel.Landed = useTarget ? _to.Landed : _from.Landed;
            vessel.Splashed = useTarget ? _to.Splashed : _from.Splashed;
            vessel.latitude = Lerp(_from.Latitude, _to.Latitude, pct);
            vessel.longitude = LerpAngle(_from.Longitude, _to.Longitude, pct);
            vessel.altitude = Lerp(_from.Altitude, _to.Altitude, pct);

            var worldRotation = (Quaternion)body.rotation * rotation;
            var onSurface = _to.Situation <= (byte)Vessel.Situations.FLYING;
            var position = onSurface
                ? body.GetWorldSurfacePosition(vessel.latitude, vessel.longitude, vessel.altitude)
                : vessel.orbit.getPositionAtUT(ut);
            SetPositionAndRotation(vessel, position, worldRotation);
        }

        private static void SetPositionAndRotation(Vessel vessel, Vector3d position, Quaternion rotation)
        {
            if (!vessel.loaded)
            {
                vessel.vesselTransform.position = position;
                vessel.vesselTransform.rotation = rotation;
                return;
            }
            var parts = vessel.parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var partRotation = rotation * part.orgRot;
                part.partTransform.rotation = partRotation;
                if (vessel.packed || part.physicalSignificance == Part.PhysicalSignificance.FULL)
                    part.partTransform.position = position + (Vector3d)(rotation * part.orgPos);
                if (!vessel.packed && part.rb != null)
                {
                    part.rb.rotation = partRotation;
                    if (part.physicalSignificance == Part.PhysicalSignificance.FULL) part.rb.position = part.partTransform.position;
                }
                part.ResumeVelocity();
            }
        }

        private static void SetOrbit(Orbit orbit, ref VesselStateMsg state)
        {
            var body = GetBody(state.BodyIndex);
            orbit.SetOrbit(state.Inclination, state.Eccentricity, state.SemiMajorAxis, state.Lan, state.ArgumentOfPeriapsis, state.MeanAnomalyAtEpoch, state.Epoch, body);
        }

        private static Quaternion Rotation(ref VesselStateMsg state) => new Quaternion(state.RotX, state.RotY, state.RotZ, state.RotW);

        private static CelestialBody GetBody(int index)
        {
            var bodies = FlightGlobals.Bodies;
            return bodies != null && index >= 0 && index < bodies.Count ? bodies[index] : null;
        }

        private static double Lerp(double a, double b, float t) => a + (b - a) * t;

        private static double LerpAngle(double a, double b, float t)
        {
            var delta = ((b - a + 540.0) % 360.0) - 180.0;
            return a + delta * t;
        }
    }
}
