using KspMp.Shared.Protocol;
using UnityEngine;

namespace KspMp.Vessels
{
    public static class VesselStateCapture
    {
        /// <summary>Snapshots a vessel we simulate into a body-relative state message.</summary>
        public static VesselStateMsg Capture(Vessel vessel, double ut)
        {
            var body = vessel.mainBody;
            var orbit = vessel.orbit;
            var surfaceVelocity = Quaternion.Inverse(body.bodyTransform.rotation) * (Vector3)vessel.srf_velocity;
            var rotation = vessel.srfRelRotation;
            var angularVelocity = vessel.angularVelocity;
            return new VesselStateMsg
            {
                VesselId = vessel.id,
                Ut = ut,
                BodyIndex = (ushort)body.flightGlobalsIndex,
                Situation = (byte)vessel.situation,
                Landed = vessel.Landed,
                Splashed = vessel.Splashed,
                Latitude = vessel.latitude,
                Longitude = vessel.longitude,
                Altitude = vessel.altitude,
                HeightFromTerrain = (float)vessel.heightFromTerrain,
                SrfVelX = surfaceVelocity.x, SrfVelY = surfaceVelocity.y, SrfVelZ = surfaceVelocity.z,
                RotX = rotation.x, RotY = rotation.y, RotZ = rotation.z, RotW = rotation.w,
                AngVelX = angularVelocity.x, AngVelY = angularVelocity.y, AngVelZ = angularVelocity.z,
                Inclination = orbit.inclination,
                Eccentricity = orbit.eccentricity,
                SemiMajorAxis = orbit.semiMajorAxis,
                Lan = orbit.LAN,
                ArgumentOfPeriapsis = orbit.argumentOfPeriapsis,
                MeanAnomalyAtEpoch = orbit.meanAnomalyAtEpoch,
                Epoch = orbit.epoch,
            };
        }
    }
}
