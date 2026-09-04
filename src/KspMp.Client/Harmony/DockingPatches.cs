using HarmonyLib;
using KspMp.Systems;

namespace KspMp.Harmony
{
    internal static class DockingGate
    {
        /// <summary>True when docking between these two vessels may proceed locally (we simulate both).</summary>
        public static bool Allowed(Vessel a, Vessel b)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Network == null || !addon.Network.IsConnected || addon.Dock == null || a == null || b == null) return true;
            var registry = addon.Vessels;
            if (!registry.IsKnown(a.id) && !registry.IsKnown(b.id)) return true;
            if (registry.IsMine(a.id) && registry.IsMine(b.id)) return true;
            var mine = registry.IsMine(a.id) ? a : registry.IsMine(b.id) ? b : null;
            if (mine == null) return true;   // neither side is ours to simulate; leave it to whoever owns them
            var other = mine == a ? b : a;
            if (!DockSystem.CanDock(mine) || !DockSystem.CanDock(other))
            {
                // Debris bumping into a ship is a collision, not a docking; let KSP handle it locally.
                return true;
            }
            addon.Dock.SendIntent(mine.id, other.id, (float)(a.GetWorldPos3D() - b.GetWorldPos3D()).magnitude);
            ScreenMessages.PostScreenMessage("Docking: waiting for the physics hand-off ...", 2f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Docking deferred: " + a.GetDisplayName() + " and " + b.GetDisplayName() + " are not simulated by the same client yet");
            return false;
        }
    }

    [HarmonyPatch(typeof(ModuleDockingNode), nameof(ModuleDockingNode.DockToVessel), typeof(ModuleDockingNode))]
    internal static class ModuleDockingNode_DockToVessel
    {
        private static bool Prefix(ModuleDockingNode __instance, ModuleDockingNode node) => DockingGate.Allowed(__instance != null ? __instance.vessel : null, node != null ? node.vessel : null);
    }

    [HarmonyPatch(typeof(Part), nameof(Part.Couple), typeof(Part))]
    internal static class Part_Couple
    {
        private static bool Prefix(Part __instance, Part tgtPart) => DockingGate.Allowed(__instance != null ? __instance.vessel : null, tgtPart != null ? tgtPart.vessel : null);
    }
}
