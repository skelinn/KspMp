using System;
using System.Collections.Generic;
using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>
    /// KSP deletes any vessel that is on rails, not the active one, and in the atmosphere, on the grounds that
    /// nothing it does not simulate can fly through air (Vessel.CheckKill, called from VesselPrecalculate).
    /// That is exactly what another player's rocket is on this machine once it climbs out of physics range:
    /// it blinked out, and came back with the next snapshot thirty seconds later, all the way up. Somebody else
    /// simulates it and streams where it is, so it lives.
    /// </summary>
    [HarmonyPatch(typeof(Vessel), nameof(Vessel.CheckKill))]
    internal static class Vessel_CheckKill
    {
        private static readonly HashSet<Guid> Reported = new HashSet<Guid>();

        private static bool Prefix(Vessel __instance)
        {
            if (!MultiplayerGuard.Connected || __instance == null) return true;
            var addon = KspMpAddon.Instance;
            if (addon.Vessels == null || !addon.Vessels.IsOwnedByOther(__instance.id)) return true;
            if (__instance.packed && !__instance.LandedOrSplashed && __instance.mainBody != null
                && __instance.mainBody.GetPressure(__instance.altitude) > 1.0 && Reported.Add(__instance.id))
                Log.Info("Keeping " + __instance.GetDisplayName() + " alive on rails in the atmosphere: somebody else flies it");
            return false;
        }
    }
}
