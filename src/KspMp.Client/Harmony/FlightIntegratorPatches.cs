using HarmonyLib;
using KspMp.Vessels;

namespace KspMp.Harmony
{
    /// <summary>
    /// Keeps KSP's flight integrator off a vessel somebody else simulates.
    ///
    /// Disabling the component once was not enough and never had been. Vessel.Update calls
    /// UpdateVesselModuleActivation every frame, which is "if the module's enabled flag disagrees with
    /// ShouldBeActive, flip it", and FlightIntegrator.ShouldBeActive is simply "is the vessel loaded". So the
    /// integrator we switched off in the physics step came back on in the same frame, every frame, and every
    /// replica in the sky was running full stock aerodynamics and heating while being teleported along its
    /// owner's path. On re-entry that meant other people's craft overheating and shedding parts on your
    /// machine while they saw nothing wrong.
    /// </summary>
    [HarmonyPatch(typeof(FlightIntegrator), nameof(FlightIntegrator.ShouldBeActive))]
    internal static class FlightIntegrator_ShouldBeActive
    {
        private static void Postfix(FlightIntegrator __instance, ref bool __result)
        {
            if (!__result) return;
            var vessel = __instance != null ? __instance.Vessel : null;
            if (vessel != null && VesselImmortal.IsImmortal(vessel)) __result = false;
        }
    }
}
