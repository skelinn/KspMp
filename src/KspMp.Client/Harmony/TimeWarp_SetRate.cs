using HarmonyLib;
using KspMp.Shared.Protocol;
using KspMp.Systems;

namespace KspMp.Harmony
{
    /// <summary>Warp changes become requests to the server; only the server's answer actually changes the rate.</summary>
    [HarmonyPatch(typeof(TimeWarp), nameof(TimeWarp.SetRate), typeof(int), typeof(bool), typeof(bool))]
    internal static class TimeWarp_SetRate
    {
        private static bool Prefix(int rate_index, bool instant, bool postScreenMessage)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Network == null || !addon.Network.IsConnected || addon.Warp == null) return true;
            if (WarpSystem.ApplyingServerState || addon.Warp.InSceneGrace) return true;
            var mode = TimeWarp.fetch != null && TimeWarp.fetch.Mode == TimeWarp.Modes.LOW ? WarpMode.Physics : WarpMode.Rails;
            addon.Warp.RequestFromUser(mode, rate_index);
            return false;
        }
    }
}
