using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>
    /// For vessels driven by a replica, KSP must keep the orbit-derived position and velocity coherent but must not
    /// reposition the vessel itself (the replica does that every physics step). Everything else runs stock.
    /// Port of LunaMultiplayer's OrbitDriver_UpdateFromParameters (MIT).
    /// </summary>
    [HarmonyPatch(typeof(OrbitDriver), "updateFromParameters", typeof(bool))]
    internal static class OrbitDriver_UpdateFromParameters
    {
        private static bool Prefix(OrbitDriver __instance)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Network == null || !addon.Network.IsConnected || __instance.vessel == null) return true;
            if (!addon.Vessels.TryGet(__instance.vessel.id, out var remote) || remote.Replica == null || !remote.Replica.HasPendingUpdates) return true;

            __instance.updateUT = Planetarium.GetUniversalTime();
            __instance.orbit.UpdateFromUT(__instance.updateUT);
            __instance.pos = __instance.orbit.pos;
            __instance.vel = __instance.orbit.vel;
            __instance.pos.Swizzle();
            __instance.vel.Swizzle();
            return false;
        }
    }
}
