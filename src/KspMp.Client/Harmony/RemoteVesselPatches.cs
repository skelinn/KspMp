using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>
    /// KSP deletes any vessel that is on rails, not the active one, and in the atmosphere, on the grounds that
    /// nothing it does not simulate can fly through air (Vessel.CheckKill, called from VesselPrecalculate).
    /// That is exactly what another player's rocket is on this machine once it climbs out of physics range:
    /// it blinked out, and came back with the next snapshot thirty seconds later, all the way up. Somebody else
    /// simulates it and streams where it is, so it lives. The prefix runs only when that branch would fire,
    /// and then skips the whole method (a prefix cannot skip one branch) - including the below-terrain check,
    /// which is fine: the owner's states put the replica where the owner has it.
    /// </summary>
    [HarmonyPatch(typeof(Vessel), nameof(Vessel.CheckKill))]
    internal static class Vessel_CheckKill
    {
        private static bool Prefix(Vessel __instance)
        {
            if (!MultiplayerGuard.Connected || __instance == null) return true;
            if (!__instance.packed || __instance.LandedOrSplashed || __instance.mainBody == null) return true;
            if (__instance.mainBody.GetPressure(__instance.altitude) <= 1.0) return true;
            var registry = KspMpAddon.Instance.Vessels;
            if (registry == null || !registry.TryGet(__instance.id, out var remote) || !registry.IsOwnedByOther(remote)) return true;
            if (!remote.KeepAliveLogged)
            {
                remote.KeepAliveLogged = true;
                Log.Info("Keeping " + __instance.GetDisplayName() + " alive on rails in the atmosphere: somebody else flies it");
            }
            return false;
        }
    }
}
